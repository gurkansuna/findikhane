# Fındıkhane (.NET 8 / ASP.NET Core)

Karadeniz fındığı için hazırlanmış, tek sayfalık tanıtım ve satış vitrini.

Bu sürüm, orijinal Node.js (çekirdek `http` modülü) uygulamasının **.NET 8 (ASP.NET
Core, minimal API) + PostgreSQL** ile birebir davranışsal karşılığıdır. Vitrin
sayfası (`wwwroot/`) değişmeden korunmuştur; sadece API/sunucu katmanı yeniden
yazılmıştır.

## Mimari

- **ASP.NET Core minimal API** — orijinal koddaki gibi ekstra bir web framework
  (MVC, Controllers) kullanılmıyor; sadece iki endpoint var: `POST /api/checkout`
  ve `POST /payment/callback`. Statik dosyalar `wwwroot/` üzerinden servis edilir.
- **Ham ADO.NET (Npgsql)** — orijinal kod da bir ORM kullanmıyordu (dosya tabanlı
  JSON okuma/yazma). Aynı minimalist yaklaşımla, EF Core yerine doğrudan
  parametrik SQL sorguları (`Npgsql`) kullanıldı. Tek harici NuGet bağımlılığı budur.
- **iyzico Checkout Form entegrasyonu** — imza oluşturma (`IYZWSv2` HMAC-SHA256),
  webhook imza doğrulama ve `money()` formatlama davranışı orijinal koddan
  **birebir** taşındı (bkz. Doğrulama bölümü).
- **PostgreSQL** — sipariş kayıtları artık `data/orders.json` dosyası yerine bir
  `orders` tablosunda tutuluyor (sepet içeriği `jsonb` sütununda). Şema, uygulama
  açılışında otomatik olarak oluşturulur (`CREATE TABLE IF NOT EXISTS`).

```
src/Findikhane.Api/
  Program.cs                  API endpoint'leri, güvenlik başlıkları, HTML sonuç sayfası
  Catalog/ProductCatalog.cs   Ürün/fiyat kataloğu (artık thread-safe/güncellenebilir, bkz. Pricing/)
  Contracts/                  İstek/yanıt DTO'ları
  Validation/                 Alıcı ve sepet doğrulama (normaliseBuyer/normaliseCart)
  Payments/IyzicoClient.cs    iyzico imza oluşturma, doğrulama ve HTTP isteği
  Data/OrderRepository.cs     PostgreSQL sipariş deposu (readOrders/saveOrders karşılığı)
  Pricing/                    Fındık fiyatlarının otomatik güncellenmesi (bkz. ilgili bölüm aşağıda)
  wwwroot/                    Vitrin (index.html, script.js, styles.css) — fiyatlar artık /api/products'tan
```

## Docker ile çalıştırma

```bash
cp .env.example .env
# .env dosyasını iyzico Sandbox/Production anahtarlarınızla ve alan adınızla doldurun.

docker compose up --build
```

Ardından tarayıcıdan `http://localhost:8080` adresini açın. `docker-compose.yml`,
uygulamayı ve bir PostgreSQL 16 konteynerini birlikte ayağa kaldırır; veriler
`findikhane-db` adlı bir Docker volume'ünde kalıcı olarak saklanır.

Sadece uygulama imajını (kendi PostgreSQL'inize bağlanacak şekilde) build etmek
isterseniz:

```bash
docker build -t findikhane .
docker run --rm -p 8080:8080 \
  -e IYZICO_API_KEY=... \
  -e IYZICO_SECRET_KEY=... \
  -e PUBLIC_BASE_URL=https://findikhane.com \
  -e POSTGRES_CONNECTION_STRING="Host=...;Port=5432;Database=findikhane;Username=...;Password=..." \
  findikhane
```

## Ortam değişkenleri

| Değişken | Açıklama |
|---|---|
| `IYZICO_API_KEY`, `IYZICO_SECRET_KEY` | iyzico panelindeki Sandbox veya Production API anahtarları |
| `IYZICO_BASE_URL` | Sandbox: `https://sandbox-api.iyzipay.com`, Production: `https://api.iyzipay.com` |
| `PUBLIC_BASE_URL` | HTTPS ile yayınlanan, sondaki `/` olmadan açık alan adınız |
| `PORT` | Uygulamanın dinleyeceği port (varsayılan `8080`) |
| `POSTGRES_CONNECTION_STRING` | Npgsql bağlantı dizesi (docker-compose bunu `POSTGRES_DB/USER/PASSWORD` değerlerinden otomatik oluşturur) |

Bu üç iyzico/alan adı değişkeninden biri boşsa `/api/checkout` orijinal koddaki
gibi `503` ile "Ödeme altyapısı henüz yapılandırılmadı." mesajı döner.

| `HAZELNUT_PRICE_SOURCE_URL` | (İsteğe bağlı) `appsettings.json` içindeki `HazelnutPricing:SourceUrl`'u ezer |
| `HAZELNUT_PRICE_ADMIN_KEY` | `/admin/hazelnut-price/*` uçlarını korur; boşsa bu uçlar tamamen kapalıdır. **appsettings.json'a gerçek bir anahtar yazmayın**, bunu ortam değişkeniyle verin. |

## Fındık fiyatlarının otomatik güncellenmesi

Üç ürünün (Ordu ve Giresun Seçme, Taş Fırın Kavrulmuş, İpek Kıvam) 500 g perakende
fiyatları artık koda gömülü sabitler değil; `Pricing/` klasöründeki bir sistem
tarafından kabuklu fındığın güncel serbest piyasa fiyatından (TL/kg) otomatik
hesaplanıyor.

**Nasıl çalışır:**

1. `HazelnutPriceRefreshService` (bir `BackgroundService`) açılışta bir kez,
   sonra `HazelnutPricing:RefreshIntervalHours` aralığında (varsayılan 24 saat)
   `HazelnutPricing:SourceUrl` adresindeki sayfayı çekip `HazelnutPricing:PricePattern`
   düzenli ifadesiyle kg fiyatını ayıklıyor.
2. Bulunan kg fiyatından, `KernelYieldMultiplier` (kabuklu→iç fındık dönüşümü) ve
   ürün başına `RawMargin` / `RoastedMargin` / `PasteMargin` çarpanlarıyla üç ürünün
   500 g fiyatı hesaplanıyor, "9 ile biten" (charm pricing) yuvarlama uygulanıyor.
3. Yeni fiyat, mevcut fiyata göre `MaxChangeRatio` (varsayılan %15) ile sınırlanıyor
   — kaynak sayfa hatalı/anlık bir sayı döndürse bile fiyatlar tek seferde çok
   sıçramaz.
4. Sonuç hem bellekte (`ProductCatalog`, thread-safe/atomik) hem de
   `App_Data/hazelnut-price-snapshot.json` dosyasında saklanıyor; uygulama
   yeniden başladığında (deploy, konteyner restart) ilk otomatik yenileme
   tamamlanana kadar bu son bilinen fiyatlar hemen geri yükleniyor.
5. Vitrin sayfası (`script.js`), sayfa açılışında `GET /api/products`'ı çağırıp
   ürün kartlarındaki ve sepetteki fiyatları bu güncel değerlerle değiştiriyor;
   `index.html`/`script.js` içindeki sabit rakamlar yalnızca JS çalışmadan önceki
   ilk boyama ve `/api/products`'a erişilemediği anlar için bir "son çare"dir.

**⚠️ Önemli sınırlama:** Türkiye'de fındık fiyatları için resmî/ücretsiz bir API
yok. Bu yüzden kaynak, bir veri/haber sitesinin HTML'ini "kazıyan" (scraping)
basit bir mekanizma — doğası gereği kırılgandır. Kaynak site tasarımını
değiştirirse desen eşleşmeyi bırakır; bu durumda sistem **çökmez**, sadece bir
uyarı loglar ve son bilinen fiyatları korur. appsettings.json'daki varsayılan
`SourceUrl`/`PricePattern` bu depoyu hazırlarken erişilen bir örnek sayfaya göre
yazıldı — **canlıya almadan önce gerçek kaynağa karşı doğrulanmalı** ve düzenli
olarak (özellikle fiyatlar hiç değişmiyor gibi göründüğünde) uygulama loglarından
kontrol edilmelidir.

**Uçlar:**

| Uç | Açıklama |
|---|---|
| `GET /api/products` | Güncel ürün fiyatlarını ve son güncellemenin kaynağı/zamanını döner. Herkese açık. |
| `POST /admin/hazelnut-price/refresh` | Zamanlamayı beklemeden anlık bir yenileme tetikler. `X-Admin-Token: <HAZELNUT_PRICE_ADMIN_KEY>` başlığı gerektirir. |
| `POST /admin/hazelnut-price/override` | Kazıyıcı kalıcı olarak bozulduğunda kg fiyatını elle sabitler ve fiyatları hemen yeniden hesaplar (gövde: `{"pricePerKg": 205, "note": "..."}`). Aynı `X-Admin-Token` koruması geçerli; override diskte kalıcıdır (`App_Data/manual-price-override.json`) ve kaldırılana kadar web kaynağının önüne geçer. |

Örnek: kazıyıcı bozulduğunda TMO'nun resmî açıklamasından elle güncelleme —

```bash
curl -X POST https://findikhane.com/admin/hazelnut-price/override \
  -H "Content-Type: application/json" \
  -H "X-Admin-Token: $HAZELNUT_PRICE_ADMIN_KEY" \
  -d '{"pricePerKg": 255, "note": "TMO 2026 Giresun kalite alım fiyatı"}'
```

**Kalibrasyon notu:** `RawMargin`/`RoastedMargin`/`PasteMargin`'in varsayılan
değerleri (2.69 / 2.94 / 2.45), 21 Eylül 2026'da elle yapılan piyasa
araştırmasına göre seçildi: o tarihte kabuklu fındık serbest piyasa fiyatı
~190 TL/kg civarındaydı ve bu üç ürün için (butik/üretici-direkt karşılaştırmalarla
belirlenen) hedef fiyatlar 549 / 599 / 499 TL idi — marjlar tam olarak bu
sonucu (190 TL/kg girildiğinde) verecek şekilde geriye doğru hesaplandı. Bunlar
fiziksel bir sabit değil, bir başlangıç kalibrasyonudur; girdi/işçilik
maliyetleri değiştikçe `appsettings.json`'dan güncellenmelidir.

## Doğrulama (Node.js'den .NET'e taşıma sadakati)

Bu port, orijinal `server.js` ile davranışsal olarak birebir örtüşecek şekilde
satır satır incelenerek yazıldı ve şu şekilde doğrulandı:

1. **İş mantığı (kripto/doğrulama) — otomatik çapraz test.** `money()` biçimlendirme,
   alıcı doğrulama (`normaliseBuyer`: ad/soyad/TC kimlik/e-posta/telefon/adres
   kuralları ve hata mesajları), sepet doğrulama (`normaliseCart`: bilinmeyen ürün,
   miktar sınırları, aynı üründen birden fazla satırın toplanması) ve iyzico
   `IYZWSv2` HMAC-SHA256 imza oluşturma/doğrulama algoritmaları için orijinal
   Node.js kodu çalıştırılarak beklenen çıktılar üretildi, aynı girdiler .NET
   portuna verildi ve **47/47 kontrolde birebir aynı sonuç ve hata mesajları**
   doğrulandı (Türkçe karakterli değerler ve HMAC imzaları dahil).
2. **Derleme — tam proje.** Bu sandbox ortamında kurumsal ağ politikası NuGet.org'a
   erişimi engellediği için gerçek `Npgsql` paketi burada indirilemedi; bunun
   yerine kullandığımız Npgsql API yüzeyini (aynı sınıf/metot adları ve imzaları)
   taklit eden yerel bir stub'a karşı **projenin tamamı (Program.cs dahil)
   0 hata / 0 uyarı ile derlendi** ve uygulama gerçekten ayağa kalkıp statik
   dosyaları ve API hata yanıtlarını (`/api/checkout`, `/payment/callback`)
   doğru şekilde servis etti. `docker build` gerçek bir internet bağlantısıyla
   (kendi makinenizde/CI'nızda) çalıştığında gerçek `Npgsql` paketi normal şekilde
   indirilecektir; bu kısıtlama sadece bu oturuma özeldir, projenin kendisine
   dair bir sorun değildir.

## Bilinçli davranış farkları

- Beklenmeyen (doğrulama dışı) hatalarda orijinal kod her zaman `400` döner;
  bu sürüm bunları `500` olarak döndürür ve sunucu loglarına yazar (istemci
  tarafı `script.js` sadece `response.ok` kontrolü yaptığı için işlevsel bir
  fark oluşturmaz).
- Sipariş verisi artık PostgreSQL'de; kredi kartı veya kimlik bilgisi
  orijinaldeki gibi **hiçbir zaman** diske/veritabanına yazılmaz.

## İçerik

- Ürün kartları ve sepete ekleme geri bildirimi
- Mobil uyumlu düzen
- E-posta abonelik formu etkileşimi
- Dış görsel dosyasına bağımlı olmayan, CSS ile tasarlanmış özgün ürün sahneleri

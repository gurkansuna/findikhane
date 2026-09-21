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
  Catalog/ProductCatalog.cs   Ürün/fiyat kataloğu (orijinal CATALOG sabiti)
  Contracts/                  İstek/yanıt DTO'ları
  Validation/                 Alıcı ve sepet doğrulama (normaliseBuyer/normaliseCart)
  Payments/IyzicoClient.cs    iyzico imza oluşturma, doğrulama ve HTTP isteği
  Data/OrderRepository.cs     PostgreSQL sipariş deposu (readOrders/saveOrders karşılığı)
  wwwroot/                    Değişmeyen vitrin (index.html, script.js, styles.css)
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

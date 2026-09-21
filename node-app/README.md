# Fındıkhane — Node.js (vanilla) sürümü

Bu klasör, `src/Findikhane.Api` altındaki .NET sürümüyle aynı işlevleri gören, **framework kullanmayan** (sadece Node.js'in yerleşik `http` modülü) bir alternatif implementasyondur. `nextjs-app/` ve `nuxt-app/` klasörleriyle birlikte, aynı ürünün üç ayrı JavaScript/TypeScript çatısındaki yazımını oluşturur.

## Kapsam

- Ürün kataloğu, sepet doğrulaması (T.C. kimlik no, e-posta, GSM), sipariş oluşturma ve iyzico Checkout Form entegrasyonu .NET sürümüyle birebir aynı davranışı taşıyacak şekilde port edildi (`src/lib/`).
- Siparişler PostgreSQL'de saklanır (`src/lib/orderRepository.js`), dosya tabanlı depolama yok.
- Statik dosyalar (`public/`) .NET sürümünün `wwwroot/` klasörüyle aynı: aynı HTML/CSS/JS, aynı ürün görselleri, aynı fiyatlar.

## Yerel çalıştırma

```bash
cp .env.example .env   # değerleri doldurun
npm install
npm start               # veya: npm run dev (dosya değişikliklerini izler)
```

PostgreSQL'e ihtiyaç var; yoksa hızlıca `docker compose up postgres` ile ayağa kaldırabilirsiniz.

## Docker ile çalıştırma

```bash
docker compose up --build
```

`docker-compose.yml`, uygulamayı ve bir PostgreSQL 16 konteynerini birlikte ayağa kaldırır. `IYZICO_*` değişkenlerini ortamınıza göre `.env` dosyasından veya `docker compose` çağrısından geçebilirsiniz.

## Ortam değişkenleri

`.env.example` dosyasına bakın: `PORT`, `POSTGRES_CONNECTION_STRING`, `IYZICO_API_KEY`, `IYZICO_SECRET_KEY`, `IYZICO_BASE_URL`, `PUBLIC_BASE_URL`.

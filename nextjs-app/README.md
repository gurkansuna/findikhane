# Fındıkhane — Next.js (App Router) sürümü

Bu klasör, `src/Findikhane.Api` altındaki .NET sürümüyle aynı işlevleri gören bir Next.js (App Router, TypeScript) implementasyonudur. `node-app/` ve `nuxt-app/` klasörleriyle birlikte, aynı ürünün üç ayrı JavaScript/TypeScript çatısındaki yazımını oluşturur.

## Kapsam

- Ürün kataloğu, sepet doğrulaması (T.C. kimlik no, e-posta, GSM), sipariş oluşturma ve iyzico Checkout Form entegrasyonu .NET sürümüyle birebir aynı davranışı taşıyacak şekilde port edildi (`src/lib/`).
- `POST /api/checkout` ve `POST /payment/callback` uçları Route Handler olarak (`src/app/api/checkout/route.ts`, `src/app/payment/callback/route.ts`) tanımlı — ikinci uç kasıtlı olarak `/api` öneki almıyor, orijinal `server.js` ile aynı URL.
- Siparişler PostgreSQL'de saklanır (`src/lib/orderRepository.ts`).
- Vitrin sayfası (`src/app/page.tsx`) orijinal `wwwroot/index.html` ile aynı içerik ve tasarımı taşır; sepet/checkout etkileşimi de React state'ine taşınmadan, orijinal `public/script.js` dosyası aynen kullanılarak çalışır (DOM'u doğrudan yöneten kod, davranış farkı riski almamak için portlanmadı).

## Yerel çalıştırma

```bash
cp .env.example .env   # değerleri doldurun
npm install
npm run dev
```

PostgreSQL'e ihtiyaç var; yoksa hızlıca `docker compose up postgres` ile ayağa kaldırabilirsiniz.

## Docker ile çalıştırma

```bash
docker compose up --build
```

`docker-compose.yml`, uygulamayı (standalone Next.js build) ve bir PostgreSQL 16 konteynerini birlikte ayağa kaldırır.

## Ortam değişkenleri

`.env.example` dosyasına bakın: `PORT`, `POSTGRES_CONNECTION_STRING`, `IYZICO_API_KEY`, `IYZICO_SECRET_KEY`, `IYZICO_BASE_URL`, `PUBLIC_BASE_URL`.

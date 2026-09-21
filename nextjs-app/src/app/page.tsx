import Script from "next/script";

export default function HomePage() {
  return (
    <>
      <div className="topline">Ücretsiz kargo · 750 TL ve üzeri siparişlerde</div>
      <header className="site-header">
        <a className="brand" href="#anasayfa" aria-label="Fındıkhane ana sayfa">
          <span className="brand-mark">F</span>
          <span>fındıkhane</span>
        </a>
        <nav aria-label="Ana menü">
          <a href="#urunler">Fındıklarımız</a>
          <a href="#hikayemiz">Hikâyemiz</a>
          <a href="#yorumlar">Yorumlar</a>
        </nav>
        <button className="cart-button" id="open-cart" type="button" aria-label="Sepeti aç">
          <span>Sepet</span>
          <span className="cart-count" id="cart-count">0</span>
        </button>
      </header>

      <main id="anasayfa">
        <section className="hero">
          <div className="hero-copy">
            <p className="eyebrow">
              <span></span> Ordu ve Giresun&apos;dan taze hasat
            </p>
            <h1>
              Bir avuç <em>iyi</em>
              <br />
              gelir.
            </h1>
            <p className="hero-text">
              Her pakette Karadeniz&apos;in serin rüzgârı, bereketli toprağı ve özenle seçilmiş fındıkları var.
            </p>
            <div className="hero-actions">
              <a className="button button-primary" href="#urunler">
                Fındıkları keşfet <span>→</span>
              </a>
              <a className="text-link" href="#hikayemiz">
                Neden Fındıkhane? <span>↗</span>
              </a>
            </div>
            <div className="hero-note">
              <span className="spark">✦</span> Kavrulmamış, katkısız, taptaze.
            </div>
          </div>
          <div className="hero-art" aria-label="Fındık illüstrasyonu">
            <div className="sun"></div>
            <div className="line line-one"></div>
            <div className="line line-two"></div>
            <div className="leaf leaf-one"></div>
            <div className="leaf leaf-two"></div>
            <div className="hazelnut nut-back"></div>
            <div className="hazelnut nut-main"><span></span></div>
            <div className="hazelnut nut-small"><span></span></div>
            <div className="ground"></div>
            <div className="harvest-tag">
              2026
              <br />
              <strong>YENİ HASAT</strong>
            </div>
          </div>
        </section>

        <section className="trust-strip" aria-label="Fındıkhane avantajları">
          <p><span>01</span> Dalından sofrana</p>
          <p><span>02</span> Günlük paketleme</p>
          <p><span>03</span> Güvenli ödeme</p>
          <p><span>04</span> Doğal lezzet</p>
        </section>

        <section className="products section" id="urunler">
          <div className="section-heading">
            <div>
              <p className="eyebrow"><span></span> En sevilenler</p>
              <h2>Fındığın en <em>iyi</em> hâli.</h2>
            </div>
            <a className="text-link" href="#urunler">Tüm ürünleri gör <span>→</span></a>
          </div>
          <div className="product-grid">
            <article className="product-card feature-card">
              <div className="product-image bag-image cream">
                <span className="badge">Çok sevilen</span>
                <div className="packaging">
                  <b>fındık<br />hane</b>
                  <i>DOĞAL İÇ<br />FINDIK</i>
                  <small>ORDU VE GİRESUN</small>
                </div>
                <img
                  className="product-illustration"
                  src="/images/findik-dogal.svg"
                  alt="Doğal iç fındık illüstrasyonu"
                  width={240}
                  height={220}
                  loading="lazy"
                />
              </div>
              <div className="product-info">
                <div>
                  <p className="product-type">Çiğ iç fındık</p>
                  <h3>Ordu ve Giresun Seçme</h3>
                </div>
                <strong>₺529</strong>
              </div>
              <button className="add-button" data-product="Ordu ve Giresun Seçme" data-product-id="giresun-secme">
                Sepete ekle <span>+</span>
              </button>
            </article>
            <article className="product-card">
              <div className="product-image bag-image green">
                <div className="packaging light">
                  <b>fındık<br />hane</b>
                  <i>KAVRULMUŞ<br />FINDIK</i>
                  <small>ORDU VE GİRESUN</small>
                </div>
                <img
                  className="product-illustration"
                  src="/images/findik-kavrulmus.svg"
                  alt="Taş fırında kavrulmuş fındık illüstrasyonu"
                  width={240}
                  height={220}
                  loading="lazy"
                />
              </div>
              <div className="product-info">
                <div>
                  <p className="product-type">Kavrulmuş iç fındık</p>
                  <h3>Taş Fırın Kavrulmuş</h3>
                </div>
                <strong>₺579</strong>
              </div>
              <button className="add-button" data-product="Taş Fırın Kavrulmuş" data-product-id="tas-firin-kavrulmus">
                Sepete ekle <span>+</span>
              </button>
            </article>
            <article className="product-card">
              <div className="product-image bag-image rust">
                <div className="packaging dark">
                  <b>fındık<br />hane</b>
                  <i>FINDIK<br />EZMESİ</i>
                  <small>%100 FINDIK</small>
                </div>
                <img
                  className="product-illustration"
                  src="/images/findik-ezmesi.svg"
                  alt="Fındık ezmesi kavanozu illüstrasyonu"
                  width={240}
                  height={220}
                  loading="lazy"
                />
              </div>
              <div className="product-info">
                <div>
                  <p className="product-type">Katkısız fındık ezmesi</p>
                  <h3>İpek Kıvam</h3>
                </div>
                <strong>₺459</strong>
              </div>
              <button className="add-button" data-product="İpek Kıvam" data-product-id="ipek-kivam">
                Sepete ekle <span>+</span>
              </button>
            </article>
          </div>
        </section>

        <section className="story section" id="hikayemiz">
          <div className="story-visual">
            <div className="story-circle">
              <span>•</span><span>•</span><span>•</span><span>•</span>
              <span>•</span><span>•</span><span>•</span><span>•</span>
            </div>
            <p>
              Karadeniz<br />usulü<br /><em>iyilik.</em>
            </p>
            <div className="branch"><i></i><i></i><i></i></div>
          </div>
          <div className="story-copy">
            <p className="eyebrow"><span></span> Bizim hikâyemiz</p>
            <h2>
              Bir bahçenin<br />anlatacak <em>çok</em><br />şeyi vardır.
            </h2>
            <p>
              Fındıkhane, Ordu ve Giresun&apos;un yamaçlarında üç kuşaktır fındık yetiştiren bir ailenin sofrasından
              doğdu. İyi bir fındığın aceleye gelmeyeceğini biliyoruz; bu yüzden her taneyi hasattan pakete kadar
              özenle takip ediyoruz.
            </p>
            <a className="button button-outline" href="#urunler">Hikâyemizi dinle <span>→</span></a>
          </div>
        </section>

        <section className="quote-section" id="yorumlar">
          <p className="eyebrow centered"><span></span> Fındıkhane sofralarda</p>
          <blockquote>&ldquo;Fındığın tazesi böyle olurmuş. Paketi açar açmaz bütün ev mis gibi koktu.&rdquo;</blockquote>
          <div className="quote-author">
            <span>ZE</span>
            <p><strong>Zeynep E.</strong><br />İstanbul</p>
          </div>
        </section>

        <section className="newsletter">
          <div>
            <p className="eyebrow"><span></span> Bahçeden haber var</p>
            <h2>İyi şeylerden<br />haberdar ol.</h2>
          </div>
          <form id="newsletter-form">
            <label className="sr-only" htmlFor="email">E-posta adresiniz</label>
            <input id="email" type="email" placeholder="E-posta adresin" required />
            <button className="button button-primary" type="submit">Kaydol <span>→</span></button>
            <p className="form-message" id="form-message" aria-live="polite"></p>
          </form>
        </section>
      </main>

      <footer>
        <a className="brand" href="#anasayfa">
          <span className="brand-mark">F</span><span>fındıkhane</span>
        </a>
        <p>© 2026 Fındıkhane. Ordu ve Giresun&apos;dan sevgiyle.</p>
        <div>
          <a href="#anasayfa">Instagram</a>
          <a href="#anasayfa">İletişim</a>
        </div>
      </footer>

      <div className="cart-overlay" id="cart-overlay" hidden></div>
      <aside className="cart-drawer" id="cart-drawer" aria-label="Alışveriş sepeti" aria-hidden="true">
        <div className="drawer-header">
          <p className="eyebrow"><span></span> Sepetin</p>
          <button className="close-drawer" id="close-cart" type="button" aria-label="Sepeti kapat">×</button>
        </div>
        <div className="cart-items" id="cart-items">
          <p className="empty-cart">Sepetin henüz boş.</p>
        </div>
        <div className="cart-total">
          <span>Toplam</span>
          <strong id="cart-total">₺0</strong>
        </div>
        <form id="checkout-form" className="checkout-form">
          <p className="checkout-title">Teslimat bilgileri</p>
          <div className="form-grid">
            <label>Ad<input name="firstName" autoComplete="given-name" required /></label>
            <label>Soyad<input name="lastName" autoComplete="family-name" required /></label>
          </div>
          <label>E-posta<input name="email" type="email" autoComplete="email" required /></label>
          <div className="form-grid">
            <label>
              Telefon
              <input name="gsmNumber" type="tel" inputMode="tel" placeholder="+905XXXXXXXXX" autoComplete="tel" required />
            </label>
            <label>
              T.C. kimlik no
              <input name="identityNumber" inputMode="numeric" pattern="[0-9]{11}" maxLength={11} autoComplete="off" required />
            </label>
          </div>
          <label>Teslimat adresi<textarea name="address" autoComplete="street-address" rows={2} required></textarea></label>
          <label>Şehir<input name="city" autoComplete="address-level2" required /></label>
          <p className="payment-note">Kart bilgileri Fındıkhane&apos;ye ulaşmadan, iyzico&apos;nun güvenli ödeme sayfasında alınır.</p>
          <button className="button button-primary checkout-button" id="checkout-button" type="submit">
            Güvenli ödemeye geç <span>→</span>
          </button>
          <p className="checkout-message" id="checkout-message" aria-live="polite"></p>
        </form>
      </aside>
      <div className="toast" id="toast" role="status" aria-live="polite"></div>

      {/* Sepet/checkout etkileşimi orijinal script.js ile birebir aynı; React state'e
          taşımak yerine (DOM'u doğrudan yöneten) orijinal dosya aynen kullanılıyor. */}
      <Script src="/script.js" strategy="afterInteractive" />
    </>
  );
}

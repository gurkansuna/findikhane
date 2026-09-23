// Bu üç fiyat yalnızca JS çalışmadan önceki ilk boyama (paint) ve /api/products'a
// erişilemediği durumlar için bir "son çare" (fallback). Gerçek fiyatlar artık
// sunucuda otomatik güncelleniyor; sayfa yüklenir yüklenmez syncLivePrices()
// bunları sunucudaki güncel değerlerle değiştirir (bkz. dosyanın sonu).
const catalog = {
  "giresun-secme": { name: "Ordu ve Giresun Seçme", price: 549 },
  "tas-firin-kavrulmus": { name: "Taş Fırın Kavrulmuş", price: 599 },
  "ipek-kivam": { name: "İpek Kıvam", price: 499 }
};

const cart = new Map();
const cartCount = document.querySelector("#cart-count");
const cartTotal = document.querySelector("#cart-total");
const cartItems = document.querySelector("#cart-items");
const cartDrawer = document.querySelector("#cart-drawer");
const cartOverlay = document.querySelector("#cart-overlay");
const checkoutForm = document.querySelector("#checkout-form");
const checkoutButton = document.querySelector("#checkout-button");
const checkoutMessage = document.querySelector("#checkout-message");
const toast = document.querySelector("#toast");

function formatPrice(value) {
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY", maximumFractionDigits: 0 }).format(value);
}

function showToast(message) {
  toast.textContent = message;
  toast.classList.add("show");
  window.setTimeout(() => toast.classList.remove("show"), 2400);
}

function renderCart() {
  const items = [...cart.entries()].map(([id, quantity]) => ({ id, quantity, ...catalog[id] }));
  const count = items.reduce((total, item) => total + item.quantity, 0);
  const total = items.reduce((sum, item) => sum + item.price * item.quantity, 0);
  cartCount.textContent = count;
  cartTotal.textContent = formatPrice(total);
  checkoutButton.disabled = count === 0;

  if (!items.length) {
    cartItems.innerHTML = '<p class="empty-cart">Sepetin henüz boş.</p>';
    return;
  }

  cartItems.innerHTML = items.map((item) => `
    <div class="cart-item">
      <div><p>${item.name}</p><small>${formatPrice(item.price)}</small></div>
      <div class="quantity-control" aria-label="${item.name} adedi">
        <button type="button" data-cart-action="decrease" data-product-id="${item.id}" aria-label="Bir azalt">−</button>
        <span>${item.quantity}</span>
        <button type="button" data-cart-action="increase" data-product-id="${item.id}" aria-label="Bir artır">+</button>
      </div>
    </div>`).join("");
}

function openCart() {
  cartDrawer.classList.add("is-open");
  cartDrawer.setAttribute("aria-hidden", "false");
  cartOverlay.hidden = false;
  requestAnimationFrame(() => cartOverlay.classList.add("is-visible"));
  document.body.classList.add("drawer-open");
}

function closeCart() {
  cartDrawer.classList.remove("is-open");
  cartDrawer.setAttribute("aria-hidden", "true");
  cartOverlay.classList.remove("is-visible");
  window.setTimeout(() => { cartOverlay.hidden = true; }, 220);
  document.body.classList.remove("drawer-open");
}

document.querySelectorAll(".add-button").forEach((button) => {
  button.addEventListener("click", () => {
    const id = button.dataset.productId;
    cart.set(id, (cart.get(id) || 0) + 1);
    renderCart();
    showToast(`${button.dataset.product} sepete eklendi.`);
  });
});

cartItems.addEventListener("click", (event) => {
  const button = event.target.closest("[data-cart-action]");
  if (!button) return;
  const id = button.dataset.productId;
  const nextQuantity = (cart.get(id) || 0) + (button.dataset.cartAction === "increase" ? 1 : -1);
  if (nextQuantity > 0) cart.set(id, nextQuantity);
  else cart.delete(id);
  renderCart();
});

document.querySelector("#open-cart").addEventListener("click", openCart);
document.querySelector("#close-cart").addEventListener("click", closeCart);
cartOverlay.addEventListener("click", closeCart);
document.addEventListener("keydown", (event) => { if (event.key === "Escape") closeCart(); });

checkoutForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  if (!cart.size) return;

  const buyer = Object.fromEntries(new FormData(checkoutForm).entries());
  const items = [...cart.entries()].map(([id, quantity]) => ({ id, quantity }));
  checkoutButton.disabled = true;
  checkoutButton.innerHTML = "Ödeme sayfası hazırlanıyor…";
  checkoutMessage.textContent = "";

  try {
    const response = await fetch("/api/checkout", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ buyer, items })
    });
    const result = await response.json();
    if (!response.ok || !result.paymentPageUrl) throw new Error(result.error || "Ödeme sayfası başlatılamadı.");
    window.location.assign(result.paymentPageUrl);
  } catch (error) {
    checkoutMessage.textContent = error.message || "Bir sorun oluştu. Lütfen tekrar deneyin.";
    checkoutButton.disabled = false;
    checkoutButton.innerHTML = 'Güvenli ödemeye geç <span>→</span>';
  }
});

document.querySelector("#newsletter-form").addEventListener("submit", (event) => {
  event.preventDefault();
  document.querySelector("#form-message").textContent = "Teşekkürler, bahçeden haberler yolda.";
  event.currentTarget.reset();
});

renderCart();

// Fındık fiyatları artık sunucuda otomatik güncelleniyor (bkz. Pricing/ klasörü);
// sayfa açılışında güncel fiyatları çekip hem ürün kartlarını hem de (sepette zaten
// bir şey varsa) sepet toplamını tazeler. /api/products'a erişilemezse yukarıdaki
// sabit "son çare" fiyatlar öylece görünmeye devam eder — sayfa asla bozulmaz.
async function syncLivePrices() {
  let data;
  try {
    const response = await fetch("/api/products");
    if (!response.ok) return;
    data = await response.json();
  } catch {
    return;
  }

  (data.products || []).forEach((product) => {
    if (!catalog[product.id]) return;
    catalog[product.id].price = product.price;

    const button = document.querySelector(`.add-button[data-product-id="${product.id}"]`);
    const priceElement = button?.closest(".product-card")?.querySelector(".product-info strong");
    if (priceElement) priceElement.textContent = `₺${product.price}`;
  });

  renderCart();

  const note = document.querySelector("#price-updated-note");
  if (note && data.lastUpdate?.updatedAtUtc) {
    const updatedAt = new Date(data.lastUpdate.updatedAtUtc);
    note.textContent = `Fiyatlar piyasaya göre otomatik güncellenir · son güncelleme: ${updatedAt.toLocaleDateString("tr-TR")}`;
  }
}

syncLivePrices();

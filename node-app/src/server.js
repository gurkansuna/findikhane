import http from "node:http";
import crypto from "node:crypto";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import pg from "pg";

import { normaliseBuyer, normaliseCart } from "./lib/validation.js";
import { iyzicoRequest, verifyIyzicoSignature } from "./lib/iyzico.js";
import { OrderRepository } from "./lib/orderRepository.js";
import { DomainError } from "./lib/domainError.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PUBLIC_DIR = path.join(__dirname, "..", "public");

// --- Yapılandırma: orijinal server.js gibi doğrudan ortam değişkenlerinden okunuyor ---
const iyzicoOptions = {
  apiKey: process.env.IYZICO_API_KEY || "",
  secretKey: process.env.IYZICO_SECRET_KEY || "",
  baseUrl: (process.env.IYZICO_BASE_URL || "https://sandbox-api.iyzipay.com").replace(/\/$/, ""),
  publicBaseUrl: (process.env.PUBLIC_BASE_URL || "").replace(/\/$/, "")
};
const isIyzicoConfigured = () => Boolean(iyzicoOptions.apiKey && iyzicoOptions.secretKey && iyzicoOptions.publicBaseUrl);
const isApiCredentialsPresent = () => Boolean(iyzicoOptions.apiKey && iyzicoOptions.secretKey);

const connectionString =
  process.env.POSTGRES_CONNECTION_STRING ||
  "postgresql://findikhane:findikhane@localhost:5432/findikhane";

const pool = new pg.Pool({ connectionString });
const orders = new OrderRepository(pool);

const PORT = Number.parseInt(process.env.PORT ?? "", 10) || 8080;

const MIME_TYPES = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".ico": "image/x-icon"
};

function applySecurityHeaders(res) {
  res.setHeader("Cache-Control", "no-store");
  res.setHeader("X-Content-Type-Options", "nosniff");
  res.setHeader("X-Frame-Options", "DENY");
  res.setHeader("Referrer-Policy", "strict-origin-when-cross-origin");
}

function writeJson(res, statusCode, payload) {
  applySecurityHeaders(res);
  res.writeHead(statusCode, { "Content-Type": "application/json; charset=utf-8" });
  res.end(JSON.stringify(payload));
}

function writeHtml(res, statusCode, html) {
  applySecurityHeaders(res);
  res.writeHead(statusCode, { "Content-Type": "text/html; charset=utf-8" });
  res.end(html);
}

async function readBody(req, limitBytes) {
  const chunks = [];
  let received = 0;
  for await (const chunk of req) {
    received += chunk.length;
    if (received > limitBytes) throw new DomainError("İstek boyutu çok büyük.");
    chunks.push(chunk);
  }
  return Buffer.concat(chunks).toString("utf8");
}

function parseFormEncoded(body) {
  const result = {};
  if (!body) return result;
  for (const pair of body.split("&")) {
    if (!pair) continue;
    const [rawKey, rawValue = ""] = pair.split("=");
    const key = decodeURIComponent(rawKey.replace(/\+/g, " "));
    const value = decodeURIComponent(rawValue.replace(/\+/g, " "));
    result[key] = value;
  }
  return result;
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/'/g, "&#39;")
    .replace(/"/g, "&quot;");
}

function randomHex(byteCount) {
  return crypto.randomBytes(byteCount).toString("hex");
}

function getRemoteAddress(req) {
  const forwarded = req.headers["x-forwarded-for"];
  if (forwarded) {
    const first = String(forwarded).split(",")[0]?.trim();
    if (first) return first;
  }
  return req.socket.remoteAddress || "127.0.0.1";
}

// ------------------------------------------------------------------------------------
// server.js#handleCheckout ile birebir aynı akış.
// ------------------------------------------------------------------------------------
async function handleCheckout(req, res) {
  if (!isIyzicoConfigured()) {
    writeJson(res, 503, { error: "Ödeme altyapısı henüz yapılandırılmadı. Lütfen mağaza yöneticisiyle iletişime geçin." });
    return;
  }

  try {
    const body = await readBody(req, 48 * 1024);

    let requestData;
    try {
      requestData = JSON.parse(body || "{}");
    } catch {
      writeJson(res, 400, { error: "Geçersiz istek." });
      return;
    }

    const buyer = normaliseBuyer(requestData.buyer);
    const cart = normaliseCart(requestData.items);

    const orderId = `FH-${Date.now()}-${randomHex(3).toUpperCase()}`;
    const total = cart.reduce((sum, item) => sum + item.lineTotal, 0);
    const conversationId = crypto.randomUUID();
    const contactName = `${buyer.firstName} ${buyer.lastName}`;

    await orders.insert({
      orderId,
      createdAt: new Date(),
      cart,
      conversationId,
      total,
      paymentStatus: "PENDING"
    });

    const address = {
      address: buyer.address,
      contactName,
      city: buyer.city,
      country: "Turkey"
    };

    const payload = {
      locale: "tr",
      conversationId,
      price: total,
      paidPrice: total,
      currency: "TRY",
      basketId: orderId,
      paymentGroup: "PRODUCT",
      callbackUrl: `${iyzicoOptions.publicBaseUrl}/payment/callback`,
      enabledInstallments: [1, 2, 3, 6, 9],
      buyer: {
        id: orderId,
        name: buyer.firstName,
        surname: buyer.lastName,
        identityNumber: buyer.identityNumber,
        email: buyer.email,
        gsmNumber: buyer.gsmNumber,
        registrationAddress: buyer.address,
        city: buyer.city,
        country: "Turkey",
        ip: getRemoteAddress(req)
      },
      shippingAddress: address,
      billingAddress: address,
      basketItems: cart.map((item) => ({
        id: item.id,
        price: item.price * item.quantity,
        name: item.quantity === 1 ? item.name : `${item.name} x${item.quantity}`,
        category1: "Fındık",
        category2: item.category,
        itemType: "PHYSICAL"
      }))
    };

    const result = await iyzicoRequest(iyzicoOptions, "/payment/iyzipos/checkoutform/initialize/auth/ecom", payload);

    if (!verifyIyzicoSignature(iyzicoOptions.secretKey, result, ["conversationId", "token"])) {
      throw new DomainError("Ödeme sağlayıcısının imzası doğrulanamadı.");
    }

    await orders.setToken(orderId, result.token || "");
    writeJson(res, 200, { paymentPageUrl: result.paymentPageUrl ?? null });
  } catch (error) {
    if (error instanceof DomainError) {
      writeJson(res, 400, { error: error.message });
      return;
    }
    console.error("Checkout işlenirken beklenmeyen hata", error);
    writeJson(res, 400, { error: error.message || "Bilinmeyen bir hata oluştu." });
  }
}

// ------------------------------------------------------------------------------------
// server.js#handlePaymentCallback ile birebir aynı akış.
// ------------------------------------------------------------------------------------
async function handlePaymentCallback(req, res) {
  const body = await readBody(req, 48 * 1024);
  const contentType = req.headers["content-type"] || "";

  let values;
  if (contentType.includes("application/json")) {
    values = body ? JSON.parse(body) : {};
  } else {
    values = parseFormEncoded(body);
  }

  const token = values.token || "";
  if (!token || !isApiCredentialsPresent()) {
    await renderPaymentResult(res, false, "Ödeme sonucu doğrulanamadı.");
    return;
  }

  try {
    const order = await orders.findByToken(token);
    if (!order) {
      await renderPaymentResult(res, false, "Sipariş bulunamadı.");
      return;
    }

    const result = await iyzicoRequest(iyzicoOptions, "/payment/iyzipos/checkoutform/auth/ecom/detail", {
      locale: "tr",
      conversationId: order.conversationId,
      token
    });

    const fields = ["paymentStatus", "paymentId", "currency", "basketId", "conversationId", "paidPrice", "price", "token"];
    const completed =
      verifyIyzicoSignature(iyzicoOptions.secretKey, result, fields) &&
      result.paymentStatus === "SUCCESS" &&
      result.basketId === order.orderId;
    const paymentId = completed ? result.paymentId ?? null : null;

    await orders.completePayment(order.orderId, completed, paymentId);
    await renderPaymentResult(
      res,
      completed,
      completed ? `Siparişiniz alındı. Sipariş numaranız: ${order.orderId}` : "Ödemeniz tamamlanamadı. Lütfen tekrar deneyin."
    );
  } catch {
    await renderPaymentResult(res, false, "Ödeme sonucu sorgulanırken bir sorun oluştu.");
  }
}

async function renderPaymentResult(res, success, message) {
  const title = success ? "Ödemeniz başarıyla alındı" : "Ödeme tamamlanamadı";
  const color = success ? "#47724e" : "#b44e2d";
  const page = `<!doctype html><html lang="tr"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${escapeHtml(title)} | Fındıkhane</title><body style="margin:0;background:#f9f4e9;color:#193d36;font-family:Arial,sans-serif"><main style="max-width:560px;margin:15vh auto;padding:48px;text-align:center"><div style="font-size:48px;color:${color}">${success ? "✓" : "×"}</div><h1 style="font-family:Georgia,serif;font-size:38px;letter-spacing:-2px">${escapeHtml(title)}</h1><p style="line-height:1.6">${escapeHtml(message)}</p><a href="/" style="display:inline-block;background:#193d36;color:white;padding:14px 20px;text-decoration:none;font-weight:bold">Mağazaya dön →</a></main></body></html>`;
  writeHtml(res, success ? 200 : 400, page);
}

// ------------------------------------------------------------------------------------
// Statik dosya sunumu (wwwroot yerine public/) — orijinal server.js#serveStatic ile
// aynı fikir: basit, framework'süz dosya sunumu.
// ------------------------------------------------------------------------------------
async function serveStatic(req, res) {
  const decodedUrl = decodeURIComponent(req.url.split("?")[0]);
  let relativePath = decodedUrl === "/" ? "/index.html" : decodedUrl;

  const filePath = path.normalize(path.join(PUBLIC_DIR, relativePath));
  if (!filePath.startsWith(PUBLIC_DIR)) {
    res.writeHead(403);
    res.end();
    return;
  }

  try {
    const data = await readFile(filePath);
    const ext = path.extname(filePath).toLowerCase();
    applySecurityHeaders(res);
    res.writeHead(200, { "Content-Type": MIME_TYPES[ext] || "application/octet-stream" });
    res.end(data);
  } catch {
    res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
    res.end("Bulunamadı");
  }
}

const server = http.createServer(async (req, res) => {
  try {
    if (req.method === "POST" && req.url === "/api/checkout") {
      await handleCheckout(req, res);
      return;
    }
    if (req.method === "POST" && req.url === "/payment/callback") {
      await handlePaymentCallback(req, res);
      return;
    }
    if (req.method === "GET" || req.method === "HEAD") {
      await serveStatic(req, res);
      return;
    }
    res.writeHead(405);
    res.end();
  } catch (error) {
    console.error("Beklenmeyen sunucu hatası", error);
    if (!res.headersSent) {
      writeJson(res, 500, { error: "Beklenmeyen bir sunucu hatası oluştu." });
    }
  }
});

async function start() {
  await orders.ensureSchema();
  server.listen(PORT, "0.0.0.0", () => {
    console.log(`Fındıkhane (Node) http://0.0.0.0:${PORT} üzerinde çalışıyor`);
  });
}

start().catch((error) => {
  console.error("Sunucu başlatılamadı", error);
  process.exit(1);
});

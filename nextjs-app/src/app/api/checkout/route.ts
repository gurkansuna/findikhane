import crypto from "node:crypto";
import { NextRequest, NextResponse } from "next/server";
import { normaliseBuyer, normaliseCart } from "@/lib/validation";
import { iyzicoRequest, isIyzicoConfigured, verifyIyzicoSignature } from "@/lib/iyzico";
import { getIyzicoOptions } from "@/lib/config";
import { insertOrder, setOrderToken } from "@/lib/orderRepository";
import { DomainError } from "@/lib/domainError";

// server.js#handleCheckout ile birebir aynı akış.

function randomHex(byteCount: number): string {
  return crypto.randomBytes(byteCount).toString("hex");
}

function getRemoteAddress(request: NextRequest): string {
  const forwarded = request.headers.get("x-forwarded-for");
  if (forwarded) {
    const first = forwarded.split(",")[0]?.trim();
    if (first) return first;
  }
  return "127.0.0.1";
}

const noStoreHeaders = {
  "Cache-Control": "no-store",
  "X-Content-Type-Options": "nosniff",
  "X-Frame-Options": "DENY",
  "Referrer-Policy": "strict-origin-when-cross-origin"
};

export async function POST(request: NextRequest) {
  const options = getIyzicoOptions();

  if (!isIyzicoConfigured(options)) {
    return NextResponse.json(
      { error: "Ödeme altyapısı henüz yapılandırılmadı. Lütfen mağaza yöneticisiyle iletişime geçin." },
      { status: 503, headers: noStoreHeaders }
    );
  }

  try {
    const rawBody = await request.text();
    if (Buffer.byteLength(rawBody, "utf8") > 48 * 1024) {
      throw new DomainError("İstek boyutu çok büyük.");
    }

    let requestData: { buyer?: Record<string, unknown>; items?: unknown };
    try {
      requestData = rawBody ? JSON.parse(rawBody) : {};
    } catch {
      return NextResponse.json({ error: "Geçersiz istek." }, { status: 400, headers: noStoreHeaders });
    }

    const buyer = normaliseBuyer(requestData.buyer);
    const cart = normaliseCart(requestData.items);

    const orderId = `FH-${Date.now()}-${randomHex(3).toUpperCase()}`;
    const total = cart.reduce((sum, item) => sum + item.lineTotal, 0);
    const conversationId = crypto.randomUUID();
    const contactName = `${buyer.firstName} ${buyer.lastName}`;

    await insertOrder({
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
      callbackUrl: `${options.publicBaseUrl}/payment/callback`,
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
        ip: getRemoteAddress(request)
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

    const result = await iyzicoRequest(options, "/payment/iyzipos/checkoutform/initialize/auth/ecom", payload);

    if (!verifyIyzicoSignature(options.secretKey, result, ["conversationId", "token"])) {
      throw new DomainError("Ödeme sağlayıcısının imzası doğrulanamadı.");
    }

    await setOrderToken(orderId, (result.token as string) || "");

    return NextResponse.json({ paymentPageUrl: (result.paymentPageUrl as string) ?? null }, { headers: noStoreHeaders });
  } catch (error) {
    if (error instanceof DomainError) {
      return NextResponse.json({ error: error.message }, { status: 400, headers: noStoreHeaders });
    }
    console.error("Checkout işlenirken beklenmeyen hata", error);
    const message = error instanceof Error ? error.message : "Bilinmeyen bir hata oluştu.";
    return NextResponse.json({ error: message }, { status: 400, headers: noStoreHeaders });
  }
}

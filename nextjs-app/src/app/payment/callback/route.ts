import { NextRequest, NextResponse } from "next/server";
import { iyzicoRequest, isApiCredentialsPresent, verifyIyzicoSignature } from "@/lib/iyzico";
import { getIyzicoOptions } from "@/lib/config";
import { completeOrderPayment, findOrderByToken } from "@/lib/orderRepository";
import { renderPaymentResultPage } from "@/lib/paymentResultPage";

// server.js#handlePaymentCallback ile birebir aynı akış.

function parseFormEncoded(body: string): Record<string, string> {
  const result: Record<string, string> = {};
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

function htmlResponse(status: number, html: string) {
  return new NextResponse(html, {
    status,
    headers: {
      "Content-Type": "text/html; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
      "X-Frame-Options": "DENY",
      "Referrer-Policy": "strict-origin-when-cross-origin"
    }
  });
}

export async function POST(request: NextRequest) {
  const options = getIyzicoOptions();
  const body = await request.text();
  const contentType = request.headers.get("content-type") || "";

  let values: Record<string, unknown>;
  if (contentType.includes("application/json")) {
    values = body ? JSON.parse(body) : {};
  } else {
    values = parseFormEncoded(body);
  }

  const token = (values.token as string) || "";
  if (!token || !isApiCredentialsPresent(options)) {
    return htmlResponse(400, renderPaymentResultPage(false, "Ödeme sonucu doğrulanamadı."));
  }

  try {
    const order = await findOrderByToken(token);
    if (!order) {
      return htmlResponse(400, renderPaymentResultPage(false, "Sipariş bulunamadı."));
    }

    const result = await iyzicoRequest(options, "/payment/iyzipos/checkoutform/auth/ecom/detail", {
      locale: "tr",
      conversationId: order.conversationId,
      token
    });

    const fields = ["paymentStatus", "paymentId", "currency", "basketId", "conversationId", "paidPrice", "price", "token"];
    const completed =
      verifyIyzicoSignature(options.secretKey, result, fields) &&
      result.paymentStatus === "SUCCESS" &&
      result.basketId === order.orderId;
    const paymentId = completed ? ((result.paymentId as string) ?? null) : null;

    await completeOrderPayment(order.orderId, completed, paymentId);

    return htmlResponse(
      completed ? 200 : 400,
      renderPaymentResultPage(
        completed,
        completed ? `Siparişiniz alındı. Sipariş numaranız: ${order.orderId}` : "Ödemeniz tamamlanamadı. Lütfen tekrar deneyin."
      )
    );
  } catch {
    return htmlResponse(400, renderPaymentResultPage(false, "Ödeme sonucu sorgulanırken bir sorun oluştu."));
  }
}

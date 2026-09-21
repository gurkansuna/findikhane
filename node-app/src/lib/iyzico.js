import crypto from "node:crypto";
import { money } from "./money.js";
import { DomainError } from "./domainError.js";

// server.js'deki createAuthorization / verifyIyzicoSignature / iyzicoRequest
// fonksiyonlarının aynısı. İmza algoritması iyzico'nun IYZWSv2 şemasıyla aynıdır;
// burada davranış değişikliği yapılmamıştır.

function computeHmacHex(secretKey, data) {
  return crypto.createHmac("sha256", secretKey).update(data, "utf8").digest("hex");
}

export function createAuthorization(secretKey, apiKey, pathname, body) {
  const randomKey = crypto.randomUUID();
  const signature = computeHmacHex(secretKey, `${randomKey}${pathname}${body}`);
  const authorizationValue = `apiKey:${apiKey}&randomKey:${randomKey}&signature:${signature}`;
  const authorization = `IYZWSv2 ${Buffer.from(authorizationValue, "utf8").toString("base64")}`;
  return { authorization, randomKey };
}

function getRawFieldText(payload, field) {
  const value = payload?.[field];
  if (value === undefined || value === null) return "";
  return typeof value === "string" ? value : String(value);
}

export function verifyIyzicoSignature(secretKey, payload, fields) {
  if (typeof payload?.signature !== "string") return false;

  const joined = fields.map((field) => money(getRawFieldText(payload, field))).join(":");
  const expected = computeHmacHex(secretKey, joined);
  const actual = payload.signature.toLowerCase();

  if (actual.length !== expected.length) return false;
  return crypto.timingSafeEqual(Buffer.from(actual, "utf8"), Buffer.from(expected, "utf8"));
}

export async function iyzicoRequest(options, pathname, payload) {
  const body = JSON.stringify(payload);
  const { authorization, randomKey } = createAuthorization(options.secretKey, options.apiKey, pathname, body);

  const response = await fetch(`${options.baseUrl}${pathname}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: authorization,
      "x-iyzi-rnd": randomKey
    },
    body,
    signal: AbortSignal.timeout(15_000)
  });

  const responseBody = await response.text();

  let result;
  try {
    result = JSON.parse(responseBody);
  } catch {
    throw new DomainError("iyzico yanıtı okunamadı.");
  }

  if (!response.ok || result.status !== "success") {
    throw new DomainError(result.errorMessage || "iyzico ödeme formu başlatılamadı.");
  }

  return result;
}

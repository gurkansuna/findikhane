// data/orders.json içindeki tek dosyalı depolamanın yerini alan, PostgreSQL tabanlı
// sipariş deposu. Not: orijinal koddaki gibi kredi kartı veya kimlik bilgisi burada
// saklanmaz; sadece sepet içeriği, tutar ve iyzico ile konuşmak için gereken alanlar tutulur.

const CREATE_TABLE_SQL = `
  CREATE TABLE IF NOT EXISTS orders (
    order_id        text PRIMARY KEY,
    created_at      timestamptz NOT NULL,
    completed_at    timestamptz NULL,
    cart            jsonb NOT NULL,
    conversation_id text NOT NULL,
    total           numeric(12,2) NOT NULL,
    payment_status  text NOT NULL,
    token           text NULL,
    payment_id      text NULL
  );
  CREATE UNIQUE INDEX IF NOT EXISTS idx_orders_token ON orders (token) WHERE token IS NOT NULL;
`;

export class OrderRepository {
  constructor(pool) {
    this.pool = pool;
  }

  async ensureSchema() {
    await this.pool.query(CREATE_TABLE_SQL);
  }

  async insert(order) {
    await this.pool.query(
      `INSERT INTO orders (order_id, created_at, cart, conversation_id, total, payment_status, token)
       VALUES ($1, $2, $3, $4, $5, $6, $7)`,
      [order.orderId, order.createdAt, JSON.stringify(order.cart), order.conversationId, order.total, order.paymentStatus, order.token ?? null]
    );
  }

  async setToken(orderId, token) {
    await this.pool.query("UPDATE orders SET token = $1 WHERE order_id = $2", [token, orderId]);
  }

  async findByToken(token) {
    const { rows } = await this.pool.query(
      `SELECT order_id, created_at, completed_at, cart, conversation_id, total, payment_status, token, payment_id
       FROM orders WHERE token = $1 LIMIT 1`,
      [token]
    );
    if (rows.length === 0) return null;
    return mapRow(rows[0]);
  }

  async completePayment(orderId, completed, paymentId) {
    await this.pool.query(
      `UPDATE orders
       SET payment_status = $1, payment_id = $2, completed_at = $3
       WHERE order_id = $4`,
      [completed ? "SUCCESS" : "FAILURE", paymentId ?? null, new Date(), orderId]
    );
  }
}

function mapRow(row) {
  return {
    orderId: row.order_id,
    createdAt: row.created_at,
    completedAt: row.completed_at,
    cart: row.cart,
    conversationId: row.conversation_id,
    total: Number(row.total),
    paymentStatus: row.payment_status,
    token: row.token,
    paymentId: row.payment_id
  };
}

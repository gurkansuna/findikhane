# Fındıkhane - vanilla Node.js sürümü
FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json* ./
RUN npm install --omit=dev

FROM node:22-alpine
WORKDIR /app
ENV NODE_ENV=production
RUN addgroup -S findikhane && adduser -S findikhane -G findikhane
COPY --from=deps /app/node_modules ./node_modules
COPY package.json ./
COPY src ./src
COPY public ./public
USER findikhane
ENV PORT=8080
EXPOSE 8080
CMD ["node", "src/server.js"]

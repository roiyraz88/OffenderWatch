# Bonus B-05 — production build of the React/Vite client, served by nginx
# (not the Vite dev server) with same-origin reverse-proxying to the API —
# see nginx.conf. Build context is this directory (test-management/client).

# ---- build ----
FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# Same-origin routing (Step 4/5): the browser never talks to the "server"
# Docker service name directly (it can't resolve it) — every API/SignalR
# call goes to this page's own origin, and nginx reverse-proxies it
# server-side. An empty VITE_API_BASE_URL makes api/client.ts's
# `${API_BASE_URL}${path}` build relative URLs, which resolve against
# whatever origin the browser actually loaded the page from — a real Vite
# build-time env var (baked into the bundle here), not a runtime one.
ARG VITE_API_BASE_URL=""
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
RUN npm run build

# ---- runtime ----
FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/nginx.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80

HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=5 \
    CMD wget -q --spider http://localhost/ || exit 1

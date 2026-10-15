// Minimal service worker: caches static files only (styles, scripts, fonts, images) so the
// installed app opens faster. Pages and data always come from the server, and the live
// Blazor connection (/_blazor) is never touched. Not registered in Development (see App.razor).

const CACHE = "dual-expense-static-v1";
const STATIC_FILE = /\.(css|js|mjs|woff2?|png|svg|ico|webmanifest)$/i;

self.addEventListener("install", () => self.skipWaiting());

// Remove caches from older versions of this file.
self.addEventListener("activate", event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(keys.filter(key => key !== CACHE).map(key => caches.delete(key))))
            .then(() => self.clients.claim()));
});

self.addEventListener("fetch", event => {
    const request = event.request;
    const url = new URL(request.url);
    if (request.method !== "GET"
        || url.origin !== self.location.origin
        || url.pathname.startsWith("/_blazor")
        || !STATIC_FILE.test(url.pathname)) {
        return; // let the browser handle it normally
    }

    // Stale-while-revalidate: answer from the cache straight away, and refresh it in the background.
    event.respondWith(caches.open(CACHE).then(async cache => {
        const cached = await cache.match(request);
        const fresh = fetch(request)
            .then(response => {
                if (response.ok)
                    cache.put(request, response.clone());
                return response;
            })
            .catch(() => cached);
        return cached ?? fresh;
    }));
});

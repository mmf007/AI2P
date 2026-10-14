// SERVICE WORKER УВЕДОМЛЕНИЙ AI2P (T-422-S0). Делает ровно две вещи: показывает пришедшее
// Web Push уведомление и по касанию открывает ссылку из него (страницу быстрого ответа на
// вопрос агента или карточку задачи). Кэша и работы без сети здесь нет намеренно.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('push', event => {
    let data = {};
    try {
        data = event.data ? event.data.json() : {};
    } catch (e) {
        data = { title: 'AI2P', body: event.data ? event.data.text() : '' };
    }
    event.waitUntil(self.registration.showNotification(data.title || 'AI2P', {
        body: data.body || '',
        tag: data.tag || undefined,
        renotify: !!data.tag,
        icon: 'apple-touch-icon.png',
        data: { url: data.url || '' }
    }));
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = event.notification.data && event.notification.data.url;
    if (url) {
        event.waitUntil(self.clients.openWindow(url));
    }
});

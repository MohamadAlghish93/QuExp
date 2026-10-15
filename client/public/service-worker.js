// Minimal Web Push service worker. Registered from src/utils/pushNotifications.js.
// Runs even when the app's tab isn't focused (and on many OSes/browsers, even
// when the browser itself isn't running), which is what makes this a real
// OS-level notification rather than just an in-app one.

self.addEventListener('push', (event) => {
  let data = { title: 'Query Explorer', body: 'You have a new notification.', url: '/' }
  try {
    if (event.data) data = { ...data, ...event.data.json() }
  } catch {
    // Fall back to the default payload above if the push body isn't JSON.
  }

  event.waitUntil(
    self.registration.showNotification(data.title, {
      body: data.body,
      icon: '/favicon.svg',
      badge: '/favicon.svg',
      data: { url: data.url || '/' },
    })
  )
})

// Clicking the OS notification focuses an existing tab if one is open,
// otherwise opens a new one at the relevant page.
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const targetUrl = event.notification.data?.url || '/'

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clients) => {
      for (const client of clients) {
        if ('focus' in client) {
          client.navigate(targetUrl)
          return client.focus()
        }
      }
      return self.clients.openWindow(targetUrl)
    })
  )
})

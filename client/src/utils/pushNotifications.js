import { api } from '../api/client.js'

// Converts the VAPID public key (base64url, as issued by the server) into the
// Uint8Array format PushManager.subscribe expects.
function urlBase64ToUint8Array(base64String) {
  const padding = '='.repeat((4 - (base64String.length % 4)) % 4)
  const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/')
  const rawData = window.atob(base64)
  return Uint8Array.from([...rawData].map((char) => char.charCodeAt(0)))
}

export function isPushSupported() {
  return 'serviceWorker' in navigator && 'PushManager' in window
}

export async function getPushPermissionState() {
  if (!isPushSupported()) return 'unsupported'
  if (Notification.permission === 'denied') return 'denied'

  const registration = await navigator.serviceWorker.getRegistration()
  const existing = await registration?.pushManager.getSubscription()
  return existing ? 'subscribed' : 'not-subscribed'
}

// Registers the service worker, requests OS notification permission, subscribes
// via PushManager, and hands the subscription to the API so alerts/assignments
// can reach this browser even when the tab isn't open.
export async function enablePushNotifications() {
  if (!isPushSupported()) throw new Error('Push notifications are not supported in this browser.')

  const { publicKey } = await api.getVapidPublicKey()
  if (!publicKey) throw new Error('Push notifications are not configured on the server yet (Push:VapidPublicKey is empty).')

  const registration = await navigator.serviceWorker.register('/service-worker.js')

  const permission = await Notification.requestPermission()
  if (permission !== 'granted') throw new Error('Notification permission was not granted.')

  const subscription = await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: urlBase64ToUint8Array(publicKey),
  })

  const json = subscription.toJSON()
  await api.subscribePush({
    endpoint: json.endpoint,
    p256dh: json.keys.p256dh,
    auth: json.keys.auth,
  })
}

export async function disablePushNotifications() {
  const registration = await navigator.serviceWorker.getRegistration()
  const subscription = await registration?.pushManager.getSubscription()
  if (!subscription) return

  await api.unsubscribePush(subscription.endpoint).catch(() => {})
  await subscription.unsubscribe()
}

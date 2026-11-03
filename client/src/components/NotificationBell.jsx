import { useEffect, useState } from 'react'
import Alert from '@cloudscape-design/components/alert'
import Button from '@cloudscape-design/components/button'
import Popover from '@cloudscape-design/components/popover'
import Spinner from '@cloudscape-design/components/spinner'
import { api } from '../api/client.js'
import {
  isPushSupported, getPushPermissionState, enablePushNotifications, disablePushNotifications,
} from '../utils/pushNotifications.js'

// Polls unread count in the background; fetches the full list only when opened.
const POLL_INTERVAL_MS = 30000

export default function NotificationBell() {
  const [unreadCount, setUnreadCount] = useState(0)
  const [notifications, setNotifications] = useState([])
  const [loading, setLoading] = useState(false)
  const [pushState, setPushState] = useState('unsupported')
  const [pushError, setPushError] = useState(null)

  useEffect(() => {
    if (isPushSupported()) getPushPermissionState().then(setPushState)
  }, [])

  async function togglePush() {
    setPushError(null)
    try {
      if (pushState === 'subscribed') {
        await disablePushNotifications()
        setPushState('not-subscribed')
      } else {
        await enablePushNotifications()
        setPushState('subscribed')
      }
    } catch (e) {
      setPushError(e.message)
    }
  }

  useEffect(() => {
    let cancelled = false
    async function poll() {
      try {
        const count = await api.getUnreadNotificationCount()
        if (!cancelled) setUnreadCount(count)
      } catch {
        // Notifications are a convenience feature - a failed poll shouldn't disrupt the app.
      }
    }
    poll()
    const interval = setInterval(poll, POLL_INTERVAL_MS)
    return () => { cancelled = true; clearInterval(interval) }
  }, [])

  async function loadNotifications() {
    setLoading(true)
    try {
      setNotifications(await api.getNotifications(30))
    } finally {
      setLoading(false)
    }
  }

  async function handleMarkAllRead() {
    await api.markAllNotificationsRead()
    setUnreadCount(0)
    setNotifications((prev) => prev.map((n) => ({ ...n, isRead: true })))
  }

  async function handleMarkRead(id) {
    await api.markNotificationRead(id)
    setNotifications((prev) => prev.map((n) => (n.id === id ? { ...n, isRead: true } : n)))
    setUnreadCount((c) => Math.max(0, c - 1))
  }

  return (
    <div className="notification-bell">
      <Popover
        header="Notifications"
        size="large"
        fixedWidth
        dismissButton
        dismissAriaLabel="Close notifications"
        triggerType="custom"
        content={
          <div className="bell-dropdown">
          <div className="bell-dropdown-header">
            {notifications.some((n) => !n.isRead) && (
              <Button variant="link" onClick={handleMarkAllRead}>Mark all read</Button>
            )}
          </div>

          {pushState !== 'unsupported' && (
            <div className="bell-push-toggle">
              <span>{pushState === 'subscribed' ? '🔔 Desktop alerts on' : '🔕 Desktop alerts off'}</span>
              <Button variant="link" onClick={togglePush} disabled={pushState === 'denied'}>
                {pushState === 'denied' ? 'blocked in browser' : pushState === 'subscribed' ? 'turn off' : 'turn on'}
              </Button>
            </div>
          )}
          {pushError && <Alert type="error">{pushError}</Alert>}
          {loading ? (
            <div className="bell-empty"><Spinner /> Loading notifications…</div>
          ) : notifications.length === 0 ? (
            <p className="muted bell-empty">No notifications yet. Set up an alert to get one.</p>
          ) : (
            <ul className="bell-list">
              {notifications.map((n) => (
                <li key={n.id} className={n.isRead ? 'bell-item' : 'bell-item bell-item-unread'}>
                  <p>{n.message}</p>
                  <span className="muted">{new Date(n.createdAtUtc).toLocaleString()}</span>
                  {!n.isRead && (
                    <Button variant="link" onClick={() => handleMarkRead(n.id)}>Mark as read</Button>
                  )}
                </li>
              ))}
            </ul>
          )}
          </div>
        }
      >
        <Button
          variant="icon"
          iconName="notification"
          ariaLabel={unreadCount > 0 ? `Notifications, ${unreadCount} unread` : 'Notifications'}
          onClick={loadNotifications}
        />
        {unreadCount > 0 && <span className="bell-badge" aria-hidden="true" />}
      </Popover>
    </div>
  )
}

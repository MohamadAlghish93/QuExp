import { useEffect, useState, useRef } from 'react'
import { api } from '../api/client.js'
import {
  isPushSupported, getPushPermissionState, enablePushNotifications, disablePushNotifications,
} from '../utils/pushNotifications.js'

// Polls unread count in the background; fetches the full list only when opened.
const POLL_INTERVAL_MS = 30000

export default function NotificationBell() {
  const [unreadCount, setUnreadCount] = useState(0)
  const [open, setOpen] = useState(false)
  const [notifications, setNotifications] = useState([])
  const [loading, setLoading] = useState(false)
  const [pushState, setPushState] = useState('unsupported')
  const [pushError, setPushError] = useState(null)
  const containerRef = useRef(null)

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

  useEffect(() => {
    function handleClickOutside(e) {
      if (containerRef.current && !containerRef.current.contains(e.target)) setOpen(false)
    }
    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  async function toggleOpen() {
    const next = !open
    setOpen(next)
    if (next) {
      setLoading(true)
      try {
        setNotifications(await api.getNotifications(30))
      } finally {
        setLoading(false)
      }
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
    <div className="notification-bell" ref={containerRef}>
      <button className="bell-button" onClick={toggleOpen} aria-label="Notifications">
        🔔
        {unreadCount > 0 && <span className="bell-badge">{unreadCount > 99 ? '99+' : unreadCount}</span>}
      </button>

      {open && (
        <div className="bell-dropdown">
          <div className="bell-dropdown-header">
            <strong>Notifications</strong>
            {notifications.some((n) => !n.isRead) && (
              <button className="link-button" onClick={handleMarkAllRead}>mark all read</button>
            )}
          </div>

          {pushState !== 'unsupported' && (
            <div className="bell-push-toggle">
              <span>{pushState === 'subscribed' ? '🔔 Desktop alerts on' : '🔕 Desktop alerts off'}</span>
              <button className="link-button" onClick={togglePush} disabled={pushState === 'denied'}>
                {pushState === 'denied' ? 'blocked in browser' : pushState === 'subscribed' ? 'turn off' : 'turn on'}
              </button>
            </div>
          )}
          {pushError && <p className="error-banner bell-push-error">{pushError}</p>}
          {loading ? (
            <p className="muted bell-empty">Loading…</p>
          ) : notifications.length === 0 ? (
            <p className="muted bell-empty">No notifications yet. Set up an alert to get one.</p>
          ) : (
            <ul className="bell-list">
              {notifications.map((n) => (
                <li key={n.id} className={n.isRead ? 'bell-item' : 'bell-item bell-item-unread'}
                    onClick={() => !n.isRead && handleMarkRead(n.id)}>
                  <p>{n.message}</p>
                  <span className="muted">{new Date(n.createdAtUtc).toLocaleString()}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}

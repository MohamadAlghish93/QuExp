import { useEffect, useState } from 'react'

const SESSION_KEY = 'qe_health_checked'

// Checks every saved connection once per login session. The result is cached
// server-side per user AND guarded here by a sessionStorage flag, so navigating
// between pages doesn't re-test. sessionStorage clears when the tab closes, and
// logging out clears it explicitly.
export default function ConnectionHealthBanner({ onRefresh }) {
  const [health, setHealth] = useState(null)
  const [checking, setChecking] = useState(false)
  const [dismissed, setDismissed] = useState(false)

  async function check(refresh = false) {
    setChecking(true)
    try {
      const results = await onRefresh(refresh)
      setHealth(results)
      sessionStorage.setItem(SESSION_KEY, '1')
    } catch {
      // A failed health check shouldn't block the page - the user can still
      // run queries and will see a real error if a connection is actually down.
    } finally {
      setChecking(false)
    }
  }

  useEffect(() => {
    if (sessionStorage.getItem(SESSION_KEY)) return // already checked this session
    check(false)
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  if (dismissed || !health || health.length === 0) return null

  const failed = health.filter((h) => !h.isReachable)

  if (failed.length === 0) {
    return (
      <div className="health-banner health-ok">
        ✓ All {health.length} connection{health.length === 1 ? '' : 's'} reachable
        <button className="link-button" onClick={() => setDismissed(true)}>dismiss</button>
      </div>
    )
  }

  return (
    <div className="health-banner health-bad">
      <div>
        <strong>{failed.length} of {health.length} connection{health.length === 1 ? '' : 's'} unreachable:</strong>
        <ul>
          {failed.map((f) => (
            <li key={f.connectionId}><strong>{f.name}</strong> — {f.error}</li>
          ))}
        </ul>
      </div>
      <div className="health-actions">
        <button className="link-button" onClick={() => check(true)} disabled={checking}>
          {checking ? 'Re-testing…' : 'Re-test'}
        </button>
        <button className="link-button" onClick={() => setDismissed(true)}>dismiss</button>
      </div>
    </div>
  )
}

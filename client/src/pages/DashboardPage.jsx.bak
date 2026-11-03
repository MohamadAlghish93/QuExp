import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client.js'
import SimpleBarChart from '../components/SimpleBarChart.jsx'
import ShareDashboardModal from '../components/ShareDashboardModal.jsx'

// Landing page after login. Pin a group-by result to it from the query page's
// GroupByPanel ("📌 Pin to dashboard"). Every load re-runs each item's query
// against the live database, so it's never showing stale numbers.
export default function DashboardPage() {
  const [items, setItems] = useState(null)
  const [error, setError] = useState(null)
  const [sharingItem, setSharingItem] = useState(null)

  async function load() {
    try {
      setItems(await api.getDashboard())
    } catch (e) {
      setError(e.message)
    }
  }

  useEffect(() => { load() }, [])

  async function refreshAndKeepSharing(itemId) {
    try {
      const fresh = await api.getDashboard()
      setItems(fresh)
      if (itemId) setSharingItem(fresh.find((i) => i.id === itemId) || null)
    } catch (e) {
      setError(e.message)
    }
  }

  async function handleUnpin(id) {
    if (!confirm('Remove this tile from your dashboard?')) return
    await api.unpinFromDashboard(id)
    load()
  }

  async function handleHide(id) {
    try {
      await api.hideDashboardItem(id)
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  if (items === null) return <p className="muted">Loading dashboard…</p>

  return (
    <div className="dashboard-page">
      <div className="dashboard-header">
        <h2>Dashboard</h2>
        <Link className="link-button" to="/query">Open query editor →</Link>
      </div>

      {error && <div className="error-banner">{error}</div>}

      {items.length === 0 ? (
        <p className="muted">
          Nothing pinned yet. Run a query, use <strong>Group by column</strong>, then click{' '}
          <strong>📌 Pin to dashboard</strong> to add a tile here.
        </p>
      ) : (
        <div className="dashboard-grid">
          {items.map((item) => (
            <div key={item.id} className="dashboard-card">
              <div className="dashboard-card-header">
                <strong title={item.title}>{item.title}</strong>
                {item.isOwnedByMe ? (
                  <div className="dashboard-card-actions">
                    <button className="link-button" onClick={() => setSharingItem(item)}>share</button>
                    <button className="link-button danger" onClick={() => handleUnpin(item.id)}>unpin</button>
                  </div>
                ) : (
                  <div className="dashboard-card-actions">
                    <span className="badge badge-muted">shared by @{item.ownerUsername}</span>
                    <button className="link-button danger" onClick={() => handleHide(item.id)} title="Hide this from your dashboard only">
                      hide
                    </button>
                  </div>
                )}
              </div>
              <p className="muted dashboard-card-sub">
                {item.connectionName} · grouped by {item.groupByColumn}
                {item.isOwnedByMe && item.sharedWithUsernames?.length > 0 &&
                  ` · shared with ${item.sharedWithUsernames.length}`}
              </p>
              {item.error ? (
                <div className="error-banner">{item.error}</div>
              ) : (
                <SimpleBarChart groups={item.groups} connectionId={item.connectionId} column={item.groupByColumn} />
              )}
            </div>
          ))}
        </div>
      )}

      {sharingItem && (
        <ShareDashboardModal
          item={sharingItem}
          onShared={() => refreshAndKeepSharing(sharingItem.id)}
          onClose={() => { setSharingItem(null); load() }}
        />
      )}
    </div>
  )
}

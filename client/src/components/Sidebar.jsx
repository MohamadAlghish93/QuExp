import { useState } from 'react'
import { NavLink } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext.jsx'
import { NAV_CATALOG, ADMIN_NAV_CATALOG } from '../constants/navItems.js'

const COLLAPSE_KEY = 'qe_sidebar_collapsed'

// Full menu of every screen the user can reach, collapsible to an icon-only
// rail. Each row has a pin toggle that adds/removes it from the top bar -
// that's the "I select what needs to be in the top navigation bar" part.
export default function Sidebar({ pinnedKeys, onTogglePin }) {
  const { isAuthenticated, isAdmin, canSeeScreen } = useAuth()
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(COLLAPSE_KEY) === '1')

  if (!isAuthenticated) return null

  function toggleCollapsed() {
    setCollapsed((prev) => {
      const next = !prev
      localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0')
      return next
    })
  }

  const items = NAV_CATALOG.filter((item) => canSeeScreen(item.key))
  const adminItems = isAdmin ? ADMIN_NAV_CATALOG : []

  function renderItem(item) {
    const isPinned = pinnedKeys.includes(item.key)
    return (
      <div key={item.key} className="sidebar-row">
        <NavLink to={item.to} end={item.end} className={({ isActive }) => `sidebar-link${isActive ? ' active' : ''}`}>
          <span className="sidebar-icon">
            {item.icon}
            {collapsed && isPinned && <span className="sidebar-pinned-dot" title="Pinned to top bar" />}
          </span>
          <span className="sidebar-label">{item.label}</span>
        </NavLink>
        <button
          className={`sidebar-pin${isPinned ? ' pinned' : ''}`}
          onClick={() => onTogglePin(item.key)}
          title={isPinned ? 'Remove from top bar' : 'Pin to top bar'}
          aria-label={isPinned ? `Unpin ${item.label} from top bar` : `Pin ${item.label} to top bar`}
        >
          {isPinned ? '★' : '☆'}
        </button>
      </div>
    )
  }

  return (
    <aside className={`sidebar${collapsed ? ' collapsed' : ''}`}>
      <button
        className="icon-button sidebar-toggle"
        onClick={toggleCollapsed}
        title={collapsed ? 'Expand menu' : 'Collapse menu'}
        aria-label={collapsed ? 'Expand menu' : 'Collapse menu'}
      >
        {collapsed ? '›' : '‹'}
      </button>

      <nav className="sidebar-nav">
        {items.map(renderItem)}

        {adminItems.length > 0 && (
          <>
            <div className="sidebar-divider" />
            {!collapsed && <div className="sidebar-section-label">Admin</div>}
            {adminItems.map(renderItem)}
          </>
        )}
      </nav>

      {!collapsed && (
        <p className="sidebar-hint">Click ☆ to pin a page to the top bar.</p>
      )}
    </aside>
  )
}

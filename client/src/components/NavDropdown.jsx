import { useEffect, useRef, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'

// Groups secondary nav links under a single "Label ▾" button so the top bar
// doesn't grow a new item every time a feature adds a page. Highlights itself
// as active if the current route matches one of its own links.
export default function NavDropdown({ label, items }) {
  const [open, setOpen] = useState(false)
  const containerRef = useRef(null)
  const location = useLocation()
  const isActive = items.some((item) => location.pathname === item.to)

  useEffect(() => {
    function handleClickOutside(e) {
      if (containerRef.current && !containerRef.current.contains(e.target)) setOpen(false)
    }
    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  if (items.length === 0) return null

  return (
    <div className="nav-dropdown" ref={containerRef}>
      <button className={isActive ? 'nav-dropdown-trigger active' : 'nav-dropdown-trigger'} onClick={() => setOpen((o) => !o)}>
        {label} ▾
      </button>
      {open && (
        <div className="nav-dropdown-menu">
          {items.map((item) => (
            <NavLink key={item.key} to={item.to} onClick={() => setOpen(false)}
                     className={({ isActive: linkActive }) => (linkActive ? 'active' : '')}>
              {item.label}
            </NavLink>
          ))}
        </div>
      )}
    </div>
  )
}

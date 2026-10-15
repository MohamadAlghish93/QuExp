import { useCallback, useState } from 'react'
import { DEFAULT_PINNED_KEYS } from '../constants/navItems.js'

// Which nav items the user has chosen to also show in the top bar - purely a
// personal display preference (per browser), separate from Permissions (which
// controls what a user is even allowed to see at all). Persisted so it
// survives a reload.
const STORAGE_KEY = 'qe_pinned_nav'

export function usePinnedNav() {
  const [pinnedKeys, setPinnedKeys] = useState(() => {
    try {
      const stored = localStorage.getItem(STORAGE_KEY)
      return stored ? JSON.parse(stored) : DEFAULT_PINNED_KEYS
    } catch {
      return DEFAULT_PINNED_KEYS
    }
  })

  const togglePinned = useCallback((key) => {
    setPinnedKeys((prev) => {
      const next = prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key]
      localStorage.setItem(STORAGE_KEY, JSON.stringify(next))
      return next
    })
  }, [])

  return { pinnedKeys, togglePinned }
}

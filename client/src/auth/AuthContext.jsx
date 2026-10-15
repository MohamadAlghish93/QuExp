import { createContext, useContext, useEffect, useState, useCallback } from 'react'
import { api } from '../api/client.js'
import { ALWAYS_VISIBLE_SCREEN_KEYS } from '../constants/screens.js'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [token, setToken] = useState(() => localStorage.getItem('qe_token'))
  const [username, setUsername] = useState(() => localStorage.getItem('qe_username'))
  const [role, setRole] = useState(() => localStorage.getItem('qe_role') || 'Viewer')
  const [allowedScreens, setAllowedScreens] = useState(() => {
    try { return JSON.parse(localStorage.getItem('qe_allowed_screens') || '[]') } catch { return [] }
  })

  // If another tab logs out, reflect that here too.
  useEffect(() => {
    function onStorage(e) {
      if (e.key === 'qe_token') setToken(e.newValue)
      if (e.key === 'qe_username') setUsername(e.newValue)
    }
    window.addEventListener('storage', onStorage)
    return () => window.removeEventListener('storage', onStorage)
  }, [])

  const login = useCallback(async (usernameValue, password) => {
    const result = await api.login(usernameValue, password)
    localStorage.setItem('qe_token', result.token)
    localStorage.setItem('qe_username', result.username)
    localStorage.setItem('qe_role', result.role)
    localStorage.setItem('qe_allowed_screens', JSON.stringify(result.allowedScreens || []))
    setToken(result.token)
    setUsername(result.username)
    setRole(result.role)
    setAllowedScreens(result.allowedScreens || [])
  }, [])

  // Refreshes role/screens from the server on app load, so a permission change an
  // Admin makes while this user is logged in takes effect on next refresh - not just
  // on their next full re-login (a JWT's role claim alone would otherwise be stale
  // until it expires).
  useEffect(() => {
    if (!token) return
    api.getCurrentUser().then((me) => {
      localStorage.setItem('qe_role', me.role)
      localStorage.setItem('qe_allowed_screens', JSON.stringify(me.allowedScreens || []))
      setRole(me.role)
      setAllowedScreens(me.allowedScreens || [])
    }).catch(() => {
      // Non-fatal - keep using the role/screens from the last login until the next successful refresh.
    })
  }, [token])

  const logout = useCallback(() => {
    localStorage.removeItem('qe_token')
    localStorage.removeItem('qe_username')
    localStorage.removeItem('qe_role')
    localStorage.removeItem('qe_allowed_screens')
    // Clear the once-per-session flag so connections are re-tested on next login.
    sessionStorage.removeItem('qe_health_checked')
    setToken(null)
    setUsername(null)
    setRole('Viewer')
    setAllowedScreens([])
  }, [])

  return (
    <AuthContext.Provider value={{
      token, username, role, allowedScreens, isAuthenticated: !!token,
      isAdmin: role === 'Admin',
      canEditConnections: role === 'Admin' || role === 'Editor',
      canEditTemplates: role === 'Admin' || role === 'Editor',
      // Admins always see every screen, regardless of their own AllowedScreens list,
      // so they can never accidentally lock themselves out of the page that fixes this.
      // Screens marked alwaysVisible (Dashboard, Query) are never restrictable for anyone.
      canSeeScreen: (key) =>
        role === 'Admin' ||
        ALWAYS_VISIBLE_SCREEN_KEYS.includes(key) ||
        allowedScreens.length === 0 ||
        allowedScreens.includes(key),
      login, logout,
    }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}

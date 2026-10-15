// Presentation catalog for the sidebar/top-bar: paths + icons, keyed the same
// as constants/screens.js so canSeeScreen(key) still governs visibility.
// Adding a new page here (and to screens.js, if it should be restrictable)
// is the only step needed to make it appear in the sidebar.
export const NAV_CATALOG = [
  { key: 'dashboard', label: 'Dashboard', to: '/', end: true, icon: '🏠' },
  { key: 'query', label: 'Query', to: '/query', icon: '🧮' },
  { key: 'favorites', label: 'Favorites', to: '/favorites', icon: '★' },
  { key: 'templates', label: 'Templates', to: '/templates', icon: '📋' },
  { key: 'assignments', label: 'Assignments', to: '/assignments', icon: '👥' },
  { key: 'history', label: 'History', to: '/history', icon: '🕘' },
  { key: 'alerts', label: 'Alerts', to: '/alerts', icon: '🔔' },
  { key: 'connections', label: 'Connections', to: '/connections', icon: '🔌' },
  { key: 'pdf-layouts', label: 'PDF Layouts', to: '/pdf-layouts', icon: '📄' },
  { key: 'hidden-items', label: 'Hidden Items', to: '/hidden-items', icon: '🙈' },
  { key: 'workflow', label: 'Workflow', to: '/workflow', icon: '🔀' },
  { key: 'workflow-designs', label: 'Workflow Designs', to: '/workflow-designs', icon: '🧩' },
]

// Separate from NAV_CATALOG because these are gated by isAdmin, not canSeeScreen.
export const ADMIN_NAV_CATALOG = [
  { key: 'users', label: 'Users', to: '/users', icon: '🧑‍💼' },
  { key: 'permissions', label: 'Permissions', to: '/permissions', icon: '🛡️' },
  { key: 'audit-log', label: 'Audit Log', to: '/audit-log', icon: '📜' },
  { key: 'backup', label: 'Backup', to: '/backup', icon: '💾' },
  { key: 'error-log', label: 'Error Log', to: '/error-log', icon: '🚨' },
]

// What shows in the top bar before a user customizes it via the sidebar's pin toggle.
export const DEFAULT_PINNED_KEYS = ['dashboard', 'query', 'favorites']

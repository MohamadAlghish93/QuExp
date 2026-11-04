// Presentation catalog for the sidebar/top-bar: paths + icons, keyed the same
// as constants/screens.js so canSeeScreen(key) still governs visibility.
// Adding a new page here (and to screens.js, if it should be restrictable)
// is the only step needed to make it appear in the sidebar.
export const NAV_CATALOG = [
  { key: 'dashboard', label: 'Dashboard', to: '/', end: true, iconName: 'grid-view' },
  { key: 'query', label: 'Query', to: '/query', iconName: 'search' },
  { key: 'favorites', label: 'Favorites', to: '/favorites', iconName: 'star' },
  { key: 'templates', label: 'Templates', to: '/templates', iconName: 'folder-open' },
  { key: 'assignments', label: 'Assignments', to: '/assignments', iconName: 'group' },
  { key: 'history', label: 'History', to: '/history', iconName: 'history' },
  { key: 'alerts', label: 'Alerts', to: '/alerts', iconName: 'notification' },
  { key: 'connections', label: 'Connections', to: '/connections', iconName: 'settings' },
  { key: 'pdf-layouts', label: 'PDF Layouts', to: '/pdf-layouts', iconName: 'file' },
  { key: 'hidden-items', label: 'Hidden Items', to: '/hidden-items', iconName: 'view-full' },
  { key: 'workflow', label: 'Workflow', to: '/workflow', iconName: 'share' },
  { key: 'workflow-designs', label: 'Workflow Designs', to: '/workflow-designs', iconName: 'edit' },
]

// Separate from NAV_CATALOG because these are gated by isAdmin, not canSeeScreen.
export const ADMIN_NAV_CATALOG = [
  { key: 'users', label: 'Users', to: '/users', iconName: 'user-profile' },
  { key: 'permissions', label: 'Permissions', to: '/permissions', iconName: 'security' },
  { key: 'audit-log', label: 'Audit Log', to: '/audit-log', iconName: 'status-info' },
  { key: 'backup', label: 'Backup', to: '/backup', iconName: 'download' },
  { key: 'error-log', label: 'Error Log', to: '/error-log', iconName: 'status-negative' },
]

// What shows in the top bar before a user customizes it via the sidebar's pin toggle.
export const DEFAULT_PINNED_KEYS = ['dashboard', 'query', 'favorites']

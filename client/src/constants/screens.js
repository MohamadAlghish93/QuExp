// Every screen that can be individually shown/hidden per user via the
// Permissions page. Keys match what AuthContext.canSeeScreen() checks and what
// the Header in main.jsx uses to decide which NavLinks to render. Admins always
// see everything regardless of this list.
//
// alwaysVisible: true means this screen isn't restrictable at all - it's not
// even shown in the Permissions transfer list, and canSeeScreen() returns true
// for it unconditionally. Use this for screens every user needs by default
// (e.g. Dashboard, Query) so an Admin can't accidentally lock someone out of
// the basic ability to run a query.
export const SCREENS = [
  { key: 'dashboard', label: 'Dashboard', alwaysVisible: true },
  { key: 'query', label: 'Query', alwaysVisible: true },
  { key: 'favorites', label: 'Favorites' },
  { key: 'templates', label: 'Templates' },
  { key: 'assignments', label: 'Assignments' },
  { key: 'history', label: 'History' },
  { key: 'alerts', label: 'Alerts' },
  { key: 'connections', label: 'Connections' },
  { key: 'pdf-layouts', label: 'PDF Layouts' },
  { key: 'hidden-items', label: 'Hidden Items' },
  { key: 'workflow', label: 'Workflow' },
  { key: 'workflow-designs', label: 'Workflow Designs' },
]

export const ALWAYS_VISIBLE_SCREEN_KEYS = SCREENS.filter((s) => s.alwaysVisible).map((s) => s.key)
export const RESTRICTABLE_SCREENS = SCREENS.filter((s) => !s.alwaysVisible)

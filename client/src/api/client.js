const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000'

function authHeaders() {
  const token = localStorage.getItem('qe_token')
  return token ? { Authorization: `Bearer ${token}` } : {}
}

// Fire-and-forget: tell the server about an error the user is about to see, so admins can
// review them in one place (Admin > Error Log). Never throws, is never awaited, and never
// reports itself (that would loop). Skipped for 401 (expected session expiry), failed logins
// (user mistakes, not app problems), and for errors the server already logged itself - the
// server marks those with an X-Error-Logged header so they aren't recorded twice.
function reportClientError(method, path, status, message) {
  if (status === 401 || path.startsWith('/api/error-log') || path.startsWith('/api/auth/login')) return
  try {
    fetch(`${BASE_URL}/api/error-log/report`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify({ message, path, method, statusCode: status ?? null }),
    }).catch(() => {})
  } catch {
    // Reporting is best-effort - never let it interfere with showing the real error.
  }
}

function reportFailedResponse(res, method, path, message) {
  if (res.headers.get('X-Error-Logged') === '1') return
  reportClientError(method, path, res.status, message)
}

async function request(path, options = {}) {
  const res = await fetch(`${BASE_URL}${path}`, {
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    ...options,
  })

  if (res.status === 401) {
    // Token missing/expired - send the user back to login.
    localStorage.removeItem('qe_token')
    localStorage.removeItem('qe_username')
    if (window.location.pathname !== '/login') window.location.href = '/login'
    throw new Error('Session expired. Please log in again.')
  }

  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    const message = body.error || `Request failed: ${res.status}`
    reportFailedResponse(res, options.method || 'GET', path, message)
    throw new Error(message)
  }
  if (res.status === 204) return null
  return res.json()
}

export const api = {
  login: (username, password) =>
    request('/api/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) }),

  getCurrentUser: () => request('/api/auth/me'),

  register: (username, password) =>
    request('/api/auth/register', { method: 'POST', body: JSON.stringify({ username, password }) }),

  getConnections: () => request('/api/connections'),

  saveConnection: (payload) =>
    request('/api/connections', { method: 'POST', body: JSON.stringify(payload) }),

  deleteConnection: (id) => request(`/api/connections/${id}`, { method: 'DELETE' }),

  testConnection: (payload) =>
    request('/api/connections/test', { method: 'POST', body: JSON.stringify(payload) }),

  getConnectionHealth: (refresh = false) =>
    request(`/api/connections/health?refresh=${refresh}`),

  runPage: (payload) =>
    request('/api/query/page', { method: 'POST', body: JSON.stringify(payload) }),

  groupBy: (payload) =>
    request('/api/query/groupby', { method: 'POST', body: JSON.stringify(payload) }),

  // Downloads via an authenticated fetch (a plain <a href> can't send the
  // Authorization header) then hands the browser a Blob to save.
  exportCsv: async (connectionId, sql, parameters) => {
    const query = new URLSearchParams({
      connectionId,
      sql,
      parameters: JSON.stringify(parameters || {}),
    })
    const res = await fetch(`${BASE_URL}/api/query/export?${query.toString()}`, {
      headers: authHeaders(),
    })
    if (!res.ok) {
      const body = await res.json().catch(() => ({}))
      const message = body.error || `Export failed: ${res.status}`
      reportFailedResponse(res, 'GET', '/api/query/export', message)
      throw new Error(message)
    }
    const blob = await res.blob()
    const url = window.URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `query-result-${Date.now()}.csv`
    document.body.appendChild(a)
    a.click()
    a.remove()
    window.URL.revokeObjectURL(url)
  },

  getFavorites: (search = '', category = '') =>
    request(`/api/favorites?search=${encodeURIComponent(search)}&category=${encodeURIComponent(category)}&templatesOnly=false`),

  getTemplates: (search = '', category = '') =>
    request(`/api/favorites?search=${encodeURIComponent(search)}&category=${encodeURIComponent(category)}&templatesOnly=true`),
  hideTemplate: (id) => request(`/api/favorites/${id}/hide`, { method: 'POST' }),
  unhideTemplate: (id) => request(`/api/favorites/${id}/unhide`, { method: 'POST' }),
  getHiddenTemplates: () => request('/api/favorites/hidden'),

  renameCategory: (oldName, newName) =>
    request('/api/favorites/categories/rename', { method: 'PUT', body: JSON.stringify({ oldName, newName }) }),
  renameTag: (oldName, newName) =>
    request('/api/favorites/tags/rename', { method: 'PUT', body: JSON.stringify({ oldName, newName }) }),

  approveTemplate: (id) => request(`/api/favorites/${id}/approve`, { method: 'PUT' }),
  rejectTemplate: (id) => request(`/api/favorites/${id}/reject`, { method: 'PUT' }),
  getPendingTemplates: () => request('/api/favorites/pending'),

  // ---- Template workflows ----
  getWorkflowGroups: () => request('/api/workflows/groups'),
  getWorkflowDefinitions: () => request('/api/workflows/definitions'),
  saveWorkflowDefinition: (payload) => request('/api/workflows/definitions', { method: 'POST', body: JSON.stringify(payload) }),
  deleteWorkflowDefinition: (id) => request(`/api/workflows/definitions/${id}`, { method: 'DELETE' }),
  getWorkflowInbox: () => request('/api/workflows/inbox'),
  getWorkflowHistory: (favoriteId) => request(`/api/workflows/templates/${favoriteId}/history`),
  startWorkflow: (favoriteId, definitionId, note) =>
    request(`/api/workflows/templates/${favoriteId}/start`, { method: 'POST', body: JSON.stringify({ definitionId, note }) }),
  moveWorkflow: (favoriteId, toStepIndex, note) =>
    request(`/api/workflows/templates/${favoriteId}/move`, { method: 'POST', body: JSON.stringify({ toStepIndex, note }) }),
  detachWorkflow: (favoriteId) => request(`/api/workflows/templates/${favoriteId}`, { method: 'DELETE' }),

  getErrorLog: (source = '') => request(`/api/admin/error-log?limit=300${source ? `&source=${source}` : ''}`),
  clearErrorLog: () => request('/api/admin/error-log', { method: 'DELETE' }),

  getAuditLog: (limit = 100, skip = 0) => request(`/api/admin/audit-log?limit=${limit}&skip=${skip}`),

  exportBackup: () => request('/api/admin/backup/export'),
  restoreBackup: (bundle) => request('/api/admin/backup/restore', { method: 'POST', body: JSON.stringify(bundle) }),

  bulkMoveCategory: (ids, category) =>
    request('/api/favorites/bulk/category', { method: 'PUT', body: JSON.stringify({ ids, category }) }),
  bulkAddTag: (ids, tag) =>
    request('/api/favorites/bulk/tag', { method: 'PUT', body: JSON.stringify({ ids, tag }) }),
  bulkDeleteFavorites: (ids) =>
    request('/api/favorites/bulk', { method: 'DELETE', body: JSON.stringify({ ids }) }),

  getTemplateRecordCounts: () => request('/api/favorites/record-counts'),

  // ---- PDF layouts ----
  getPdfLayouts: (favoriteId) => request(`/api/pdf-layouts?favoriteId=${favoriteId}`),
  getPdfLayout: (id) => request(`/api/pdf-layouts/${id}`),
  savePdfLayout: (payload) => request('/api/pdf-layouts', { method: 'POST', body: JSON.stringify(payload) }),
  deletePdfLayout: (id) => request(`/api/pdf-layouts/${id}`, { method: 'DELETE' }),

  uploadPdfBackground: async (layoutId, file) => {
    const form = new FormData()
    form.append('file', file)
    const token = localStorage.getItem('qe_token')
    const res = await fetch(`${BASE_URL}/api/pdf-layouts/${layoutId}/background`, {
      method: 'POST',
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      body: form, // no Content-Type header - the browser sets the multipart boundary itself
    })
    if (!res.ok) {
      const body = await res.json().catch(() => ({}))
      const message = body.error || `Upload failed: ${res.status}`
      reportFailedResponse(res, 'POST', `/api/pdf-layouts/${layoutId}/background`, message)
      throw new Error(message)
    }
  },
  deletePdfBackground: (layoutId) => request(`/api/pdf-layouts/${layoutId}/background`, { method: 'DELETE' }),
  pdfBackgroundUrl: (layoutId) => `${BASE_URL}/api/pdf-layouts/${layoutId}/background`,

  // Downloads via an authenticated fetch (a plain link can't send the Authorization header).
  generatePdf: async (layoutId, layoutName, rowData) => {
    const res = await fetch(`${BASE_URL}/api/pdf-layouts/${layoutId}/generate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify({ rowData }),
    })
    if (!res.ok) {
      const body = await res.json().catch(() => ({}))
      const message = body.error || `PDF generation failed: ${res.status}`
      reportFailedResponse(res, 'POST', `/api/pdf-layouts/${layoutId}/generate`, message)
      throw new Error(message)
    }
    const blob = await res.blob()
    const url = window.URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${layoutName || 'record'}.pdf`
    document.body.appendChild(a)
    a.click()
    a.remove()
    window.URL.revokeObjectURL(url)
  },

  // ---- Admin: user management ----
  getAllUsersAdmin: () => request('/api/admin/users'),
  createUser: (payload) => request('/api/admin/users', { method: 'POST', body: JSON.stringify(payload) }),
  updateUserRole: (username, role) =>
    request(`/api/admin/users/${encodeURIComponent(username)}/role`, { method: 'PUT', body: JSON.stringify({ role }) }),
  updateUserPassword: (username, newPassword) =>
    request(`/api/admin/users/${encodeURIComponent(username)}/password`, { method: 'PUT', body: JSON.stringify({ newPassword }) }),
  updateUserScreens: (username, screens) =>
    request(`/api/admin/users/${encodeURIComponent(username)}/screens`, { method: 'PUT', body: JSON.stringify({ screens }) }),
  updateUserProfile: (username, payload) =>
    request(`/api/admin/users/${encodeURIComponent(username)}/profile`, { method: 'PUT', body: JSON.stringify(payload) }),
  getUserGroups: () => request('/api/admin/users/groups'),
  deleteUser: (username) => request(`/api/admin/users/${encodeURIComponent(username)}`, { method: 'DELETE' }),

  // ---- Basic user list (any logged-in user) - for assignee/access pickers ----
  getUsers: () => request('/api/users'),

  // ---- Assignments (assign a favorite to a teammate) ----
  getAssignedToMe: () => request('/api/assignments/assigned-to-me'),
  getAssignedByMe: () => request('/api/assignments/assigned-by-me'),
  createAssignment: (payload) => request('/api/assignments', { method: 'POST', body: JSON.stringify(payload) }),
  completeAssignment: (id) => request(`/api/assignments/${id}/complete`, { method: 'POST' }),
  deleteAssignment: (id) => request(`/api/assignments/${id}`, { method: 'DELETE' }),

  // ---- Browser push notifications ----
  getVapidPublicKey: () => request('/api/push/vapid-public-key'),
  subscribePush: (payload) => request('/api/push/subscribe', { method: 'POST', body: JSON.stringify(payload) }),
  unsubscribePush: (endpoint) => request('/api/push/unsubscribe', { method: 'POST', body: JSON.stringify({ endpoint }) }),

  // ---- Query history ----
  getHistory: (limit = 50, skip = 0) => request(`/api/history?limit=${limit}&skip=${skip}`),
  clearHistory: () => request('/api/history', { method: 'DELETE' }),

  // ---- Dashboard ----
  getDashboard: () => request('/api/dashboard'),
  pinToDashboard: (payload) => request('/api/dashboard', { method: 'POST', body: JSON.stringify(payload) }),
  unpinFromDashboard: (id) => request(`/api/dashboard/${id}`, { method: 'DELETE' }),
  shareDashboardItem: (id, username) =>
    request(`/api/dashboard/${id}/share`, { method: 'POST', body: JSON.stringify({ username }) }),
  unshareDashboardItem: (id, username) =>
    request(`/api/dashboard/${id}/share/${encodeURIComponent(username)}`, { method: 'DELETE' }),
  hideDashboardItem: (id) => request(`/api/dashboard/${id}/hide`, { method: 'POST' }),
  unhideDashboardItem: (id) => request(`/api/dashboard/${id}/unhide`, { method: 'POST' }),
  getHiddenDashboardItems: () => request('/api/dashboard/hidden'),

  // ---- Alerts ----
  getAlerts: () => request('/api/alerts'),
  saveAlert: (payload) => request('/api/alerts', { method: 'POST', body: JSON.stringify(payload) }),
  deleteAlert: (id) => request(`/api/alerts/${id}`, { method: 'DELETE' }),
  testAlert: (id) => request(`/api/alerts/${id}/test`, { method: 'POST' }),

  // ---- Notifications ----
  getNotifications: (limit = 30) => request(`/api/notifications?limit=${limit}`),
  getUnreadNotificationCount: () => request('/api/notifications/unread-count'),
  markNotificationRead: (id) => request(`/api/notifications/${id}/read`, { method: 'POST' }),
  markAllNotificationsRead: () => request('/api/notifications/read-all', { method: 'POST' }),

  getCategories: () => request('/api/favorites/categories'),

  getTags: () => request('/api/favorites/tags'),

  saveFavorite: (payload) =>
    request('/api/favorites', { method: 'POST', body: JSON.stringify(payload) }),

  markFavoriteUsed: (id) => request(`/api/favorites/${id}/use`, { method: 'POST' }),

  deleteFavorite: (id) => request(`/api/favorites/${id}`, { method: 'DELETE' }),
}

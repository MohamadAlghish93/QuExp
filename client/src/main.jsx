import React from 'react'
import ReactDOM from 'react-dom/client'
import { useState } from 'react'
import { BrowserRouter, Routes, Route, Navigate, useLocation, useNavigate } from 'react-router-dom'
import AppLayout from '@cloudscape-design/components/app-layout'
import TopNavigation from '@cloudscape-design/components/top-navigation'
import { AuthProvider, useAuth } from './auth/AuthContext.jsx'
import ProtectedRoute from './components/ProtectedRoute.jsx'
import AdminRoute from './components/AdminRoute.jsx'
import NotificationBell from './components/NotificationBell.jsx'
import Sidebar from './components/Sidebar.jsx'
import { usePinnedNav } from './hooks/usePinnedNav.js'
import { NAV_CATALOG, ADMIN_NAV_CATALOG } from './constants/navItems.js'
import QueryWorkspace from './pages/QueryWorkspace.jsx'
import FavoritesPage from './pages/FavoritesPage.jsx'
import LoginPage from './pages/LoginPage.jsx'
import ConnectionsPage from './pages/ConnectionsPage.jsx'
import DashboardPage from './pages/DashboardPage.jsx'
import HistoryPage from './pages/HistoryPage.jsx'
import AlertsPage from './pages/AlertsPage.jsx'
import TemplatesPage from './pages/TemplatesPage.jsx'
import UsersPage from './pages/UsersPage.jsx'
import PermissionsPage from './pages/PermissionsPage.jsx'
import AuditLogPage from './pages/AuditLogPage.jsx'
import BackupPage from './pages/BackupPage.jsx'
import ErrorLogPage from './pages/ErrorLogPage.jsx'
import AssignmentsPage from './pages/AssignmentsPage.jsx'
import PdfLayoutDesignerPage from './pages/PdfLayoutDesignerPage.jsx'
import HiddenItemsPage from './pages/HiddenItemsPage.jsx'
import WorkflowPage from './pages/WorkflowPage.jsx'
import WorkflowDesignerPage from './pages/WorkflowDesignerPage.jsx'
import '@cloudscape-design/global-styles/index.css'
import './styles.css'

function Header({ pinnedKeys }) {
  const { isAuthenticated, username, role, isAdmin, canSeeScreen, logout } = useAuth()
  const navigate = useNavigate()
  if (!isAuthenticated) return null

  const pinnedItems = [...NAV_CATALOG, ...(isAdmin ? ADMIN_NAV_CATALOG : [])]
    .filter((item) => pinnedKeys.includes(item.key) && canSeeScreen(item.key))

  return (
    <header className="app-header" id="header">
      <TopNavigation
        identity={{ title: 'Query Explorer', href: '/' }}
        utilities={[
          ...pinnedItems.map((item) => ({
            type: 'button',
            text: item.label,
            href: item.to,
            onFollow: (event) => {
              event.preventDefault()
              navigate(item.to)
            },
          })),
          {
            type: 'menu-dropdown',
            text: username,
            description: role,
            iconName: 'user-profile',
            ariaLabel: `Account: ${username}, ${role}`,
            items: [{ id: 'signout', text: 'Sign out' }],
            onItemClick: (event) => {
              if (event.detail.id === 'signout') logout()
            },
          },
        ]}
      />
      <div className="app-header-notifications">
        <NotificationBell />
      </div>
    </header>
  )
}

// QueryWorkspace is rendered here, OUTSIDE the <Routes> switch, and simply hidden
// with CSS when the active route isn't /query. That's what makes its open tabs
// (SQL text, results, everything) survive navigating to another menu item and back -
// React Router unmounting a normal <Route>'s element is exactly what was wiping the
// tabs out before. It still requires login the same as any other page.
function AppContent() {
  const { isAuthenticated } = useAuth()
  const { pinnedKeys, togglePinned } = usePinnedNav()
  const location = useLocation()
  const isQueryRoute = location.pathname === '/query'
  const [navigationOpen, setNavigationOpen] = useState(() => localStorage.getItem('qe_sidebar_collapsed') !== '1')

  return (
    <>
      <Header pinnedKeys={pinnedKeys} />
      <AppLayout
        headerSelector="#header"
        navigation={isAuthenticated && (
          <Sidebar
            pinnedKeys={pinnedKeys}
            onTogglePin={togglePinned}
            collapsed={!navigationOpen}
          />
        )}
        navigationHide={!isAuthenticated}
        navigationOpen={navigationOpen}
        navigationCloseBehavior="collapse"
        navigationCollapsedWidth={56}
        onNavigationChange={({ detail }) => {
          setNavigationOpen(detail.open)
          localStorage.setItem('qe_sidebar_collapsed', detail.open ? '0' : '1')
        }}
        ariaLabels={{
          navigation: 'Main navigation',
          navigationToggle: 'Open navigation',
          navigationClose: 'Close navigation',
          notification: 'Notifications',
        }}
        maxContentWidth={Number.MAX_VALUE}
        content={
          <main className="app-content">
          {isAuthenticated && (
            <div style={{ display: isQueryRoute ? 'block' : 'none' }}>
              <QueryWorkspace />
            </div>
          )}
          {/* Only skip the normal route switch when /query is both active AND already
              authenticated - the panel above is handling that exact case. Otherwise
              (logged out, or on any other path) Routes must run so /login and the
              catch-all redirect still work. */}
          {!(isQueryRoute && isAuthenticated) && (
            <Routes>
              <Route path="/login" element={<LoginPage />} />
              <Route path="/" element={<ProtectedRoute><DashboardPage /></ProtectedRoute>} />
              <Route path="/favorites" element={<ProtectedRoute><FavoritesPage /></ProtectedRoute>} />
              <Route path="/history" element={<ProtectedRoute><HistoryPage /></ProtectedRoute>} />
              <Route path="/alerts" element={<ProtectedRoute><AlertsPage /></ProtectedRoute>} />
              <Route path="/templates" element={<ProtectedRoute><TemplatesPage /></ProtectedRoute>} />
              <Route path="/connections" element={<ProtectedRoute><ConnectionsPage /></ProtectedRoute>} />
              <Route path="/assignments" element={<ProtectedRoute><AssignmentsPage /></ProtectedRoute>} />
              <Route path="/pdf-layouts" element={<ProtectedRoute><PdfLayoutDesignerPage /></ProtectedRoute>} />
              <Route path="/hidden-items" element={<ProtectedRoute><HiddenItemsPage /></ProtectedRoute>} />
              <Route path="/workflow" element={<ProtectedRoute><WorkflowPage /></ProtectedRoute>} />
              <Route path="/workflow-designs" element={<ProtectedRoute><WorkflowDesignerPage /></ProtectedRoute>} />
              <Route path="/users" element={<AdminRoute><UsersPage /></AdminRoute>} />
              <Route path="/permissions" element={<AdminRoute><PermissionsPage /></AdminRoute>} />
              <Route path="/audit-log" element={<AdminRoute><AuditLogPage /></AdminRoute>} />
              <Route path="/backup" element={<AdminRoute><BackupPage /></AdminRoute>} />
              <Route path="/error-log" element={<AdminRoute><ErrorLogPage /></AdminRoute>} />
              {/* Reached only when not authenticated (see condition above) - send to login. */}
              <Route path="/query" element={<Navigate to="/login" replace />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          )}
          </main>
        }
      />
    </>
  )
}

function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <AppContent />
      </AuthProvider>
    </BrowserRouter>
  )
}

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
)

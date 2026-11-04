import { useLocation, useNavigate } from 'react-router-dom'
import Button from '@cloudscape-design/components/button'
import SideNavigation from '@cloudscape-design/components/side-navigation'
import Icon from '@cloudscape-design/components/icon'
import { useAuth } from '../auth/AuthContext.jsx'
import { NAV_CATALOG, ADMIN_NAV_CATALOG } from '../constants/navItems.js'

export default function Sidebar({ pinnedKeys, onTogglePin, collapsed }) {
  const { isAuthenticated, isAdmin, canSeeScreen } = useAuth()
  const location = useLocation()
  const navigate = useNavigate()

  if (!isAuthenticated) return null

  function toNavigationItem(item) {
    const isPinned = pinnedKeys.includes(item.key)
    return {
      type: 'link',
      text: item.label,
      href: item.to,
      icon: <Icon name={item.iconName} />,
      info: (
        <Button
          variant="icon"
          iconName={isPinned ? 'star-filled' : 'star'}
          ariaLabel={isPinned ? `Unpin ${item.label} from top navigation` : `Pin ${item.label} to top navigation`}
          onClick={(event) => {
            event.stopPropagation()
            onTogglePin(item.key)
          }}
        />
      ),
    }
  }

  const items = NAV_CATALOG.filter((item) => canSeeScreen(item.key)).map(toNavigationItem)
  const adminItems = isAdmin ? ADMIN_NAV_CATALOG.map(toNavigationItem) : []
  const navigationItems = [
    { type: 'section', text: 'Navigation', items },
    ...(adminItems.length > 0
      ? [{ type: 'section', text: 'Administration', items: adminItems }]
      : []),
  ]

  return (
    <SideNavigation
      header={{ text: 'Query Explorer', href: '/' }}
      activeHref={location.pathname}
      items={navigationItems}
      collapsed={collapsed}
      onFollow={(event) => {
        event.preventDefault()
        navigate(event.detail.href)
      }}
    />
  )
}

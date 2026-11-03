import { useEffect, useState, useRef } from 'react'
import { useSearchParams } from 'react-router-dom'
import TabBar from '../components/TabBar.jsx'
import QueryTab from '../components/QueryTab.jsx'

let nextTabNumber = 1
function makeTab(overrides = {}) {
  const n = nextTabNumber++
  return { id: `tab-${Date.now()}-${n}`, label: `Query ${n}`, sql: '', connectionId: '', ...overrides }
}

// Multi-tab SQL editor, like a desktop SQL client (SSMS/DataGrip/TablePlus).
// Every open tab stays mounted (just hidden with CSS) so switching tabs never
// loses a tab's SQL, results, or scroll position. Tabs are in-memory only -
// they reset on page reload, same as most desktop query tools' unsaved state.
export default function QueryWorkspace() {
  const [searchParams, setSearchParams] = useSearchParams()
  const [tabs, setTabs] = useState(() => [makeTab()])
  const [activeId, setActiveId] = useState(() => tabs[0]?.id)
  const consumedSearchRef = useRef(null)

  // Arriving here from Favorites/History with ?sql=&connectionId=&t=<unique>
  // opens a NEW tab (t makes every navigation unique so this fires each time,
  // even for the same query run twice in a row).
  useEffect(() => {
    const favSql = searchParams.get('sql')
    const favConn = searchParams.get('connectionId')
    const token = searchParams.get('t')
    if (!favSql || token === consumedSearchRef.current) return

    consumedSearchRef.current = token
    const tab = makeTab({ sql: favSql, connectionId: favConn || '', label: 'Query' })
    setTabs((prev) => [...prev, tab])
    setActiveId(tab.id)
    setSearchParams({}, { replace: true }) // clear the URL so a refresh doesn't reopen it
  }, [searchParams, setSearchParams])

  useEffect(() => { setTabs((prev) => (prev.length ? prev : [makeTab()])) }, [])
  useEffect(() => {
    if (tabs.length && !tabs.some((t) => t.id === activeId)) setActiveId(tabs[tabs.length - 1].id)
  }, [tabs, activeId])

  function addTab() {
    const tab = makeTab()
    setTabs((prev) => [...prev, tab])
    setActiveId(tab.id)
  }

  function closeTab(id) {
    setTabs((prev) => prev.filter((t) => t.id !== id))
  }

  function renameTab(id, label) {
    setTabs((prev) => prev.map((t) => (t.id === id ? { ...t, label } : t)))
  }

  return (
    <div className="query-workspace">
      <TabBar tabs={tabs} activeId={activeId} onSelect={setActiveId} onClose={closeTab} onAdd={addTab} onRename={renameTab} />
      {tabs.map((tab) => (
        <div key={tab.id} style={{ display: tab.id === activeId ? 'block' : 'none' }}>
          <QueryTab
            initialSql={tab.sql || undefined}
            initialConnectionId={tab.connectionId || undefined}
            showHealthBanner={tab.id === tabs[0]?.id}
          />
        </div>
      ))}
    </div>
  )
}

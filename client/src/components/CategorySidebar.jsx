import { useAuth } from '../auth/AuthContext.jsx'

// Left-hand menu listing every category with a count, plus an "All" entry.
// Clicking a category filters the favorites list next to it. Admins get a
// rename (✎) affordance that renames the category everywhere it's used.
export default function CategorySidebar({ categories, counts, selected, onSelect, totalCount, onRename }) {
  const { isAdmin } = useAuth()

  function handleRenameClick(e, category) {
    e.stopPropagation()
    const next = window.prompt(`Rename category "${category}" to:`, category)
    if (next && next.trim() && next.trim() !== category) onRename(category, next.trim())
  }

  return (
    <aside className="category-sidebar">
      <h4>Categories</h4>
      <ul>
        <li>
          <button
            className={selected === '' ? 'category-item active' : 'category-item'}
            onClick={() => onSelect('')}
          >
            <span>All queries</span>
            <span className="count-badge">{totalCount}</span>
          </button>
        </li>
        {categories.map((category) => (
          <li key={category}>
            <button
              className={selected === category ? 'category-item active' : 'category-item'}
              onClick={() => onSelect(category)}
            >
              <span title={category}>{category}</span>
              {isAdmin && onRename && (
                <span className="category-rename" onClick={(e) => handleRenameClick(e, category)} title="Rename category">✎</span>
              )}
              <span className="count-badge">{counts[category] ?? 0}</span>
            </button>
          </li>
        ))}
      </ul>
      {categories.length === 0 && <p className="muted">No categories yet.</p>}
    </aside>
  )
}

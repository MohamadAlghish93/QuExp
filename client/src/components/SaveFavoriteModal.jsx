import CloudscapeInput from './CloudscapeInput.jsx'
import CloudscapeTextarea from './CloudscapeTextarea.jsx'
import CloudscapeButton from './CloudscapeButton.jsx'
import { useEffect, useMemo, useState } from 'react'
import Checkbox from '@cloudscape-design/components/checkbox'
import RadioGroup from '@cloudscape-design/components/radio-group'
import { api } from '../api/client.js'
import { useAuth } from '../auth/AuthContext.jsx'
import UserAutocomplete from './UserAutocomplete.jsx'
import Alert from '@cloudscape-design/components/alert'
import Modal from '@cloudscape-design/components/modal'

// Used both to save a NEW favorite/template (initialValues omitted) and to EDIT
// an existing one (initialValues passed in, including its id) - e.g. from the
// Templates page's Edit button.
export default function SaveFavoriteModal({ initialValues, onSave, onClose }) {
  const { canEditTemplates } = useAuth()
  const [name, setName] = useState(initialValues?.name || '')
  const [category, setCategory] = useState(initialValues?.category || '')
  const [tags, setTags] = useState(initialValues?.tags || [])
  const [tagInput, setTagInput] = useState('')
  const [isTemplate, setIsTemplate] = useState(initialValues?.isTemplate || false)
  const [description, setDescription] = useState(initialValues?.description || '')
  const [shareEveryone, setShareEveryone] = useState(
    !initialValues?.sharedWithUsernames || initialValues.sharedWithUsernames.length === 0
  )
  const [sharedWith, setSharedWith] = useState(initialValues?.sharedWithUsernames || [])
  const [dateFieldFormats, setDateFieldFormats] = useState(initialValues?.dateFieldFormats || [])
  const [existingCategories, setExistingCategories] = useState([])
  const [existingTags, setExistingTags] = useState([])
  const [error, setError] = useState(null)

  const isEditing = !!initialValues?.id

  useEffect(() => {
    Promise.all([api.getCategories(), api.getTags()])
      .then(([cats, tgs]) => {
        setExistingCategories(cats)
        setExistingTags(tgs)
      })
      .catch(() => { /* suggestions are optional - saving still works without them */ })
  }, [])

  // Tell the user whether they're reusing an existing category or creating a new one.
  const categoryStatus = useMemo(() => {
    const trimmed = category.trim()
    if (!trimmed) return null
    const match = existingCategories.find((c) => c.toLowerCase() === trimmed.toLowerCase())
    return match ? { type: 'existing', label: `Using existing category "${match}"` }
                 : { type: 'new', label: `New category "${trimmed}" will be created` }
  }, [category, existingCategories])

  const tagSuggestions = useMemo(() => {
    const needle = tagInput.trim().toLowerCase()
    return existingTags
      .filter((t) => !tags.some((added) => added.toLowerCase() === t.toLowerCase()))
      .filter((t) => needle === '' || t.toLowerCase().includes(needle))
      .slice(0, 8)
  }, [tagInput, existingTags, tags])

  function addTag(rawValue) {
    const value = rawValue.trim()
    if (!value) return
    // Reuse existing casing if this tag already exists, so we don't create near-duplicates.
    const existing = existingTags.find((t) => t.toLowerCase() === value.toLowerCase())
    const finalValue = existing || value

    if (tags.some((t) => t.toLowerCase() === finalValue.toLowerCase())) {
      setError(`Tag "${finalValue}" is already added.`)
      setTagInput('')
      return
    }
    setTags((prev) => [...prev, finalValue])
    setTagInput('')
    setError(null)
  }

  function removeTag(value) {
    setTags((prev) => prev.filter((t) => t !== value))
  }

  function handleTagKeyDown(e) {
    // Enter or comma commits the tag; Backspace on an empty box removes the last one.
    if (e.key === 'Enter' || e.key === ',') {
      e.preventDefault()
      addTag(tagInput)
    } else if (e.key === 'Backspace' && tagInput === '' && tags.length > 0) {
      removeTag(tags[tags.length - 1])
    }
  }

  function addDateFieldFormat() {
    setDateFieldFormats((prev) => [...prev, { columnName: '', sourceFormat: 'yyyyMMddHHmmss', displayFormat: 'yyyy/MM/dd HH:mm:ss' }])
  }

  function updateDateFieldFormat(index, changes) {
    setDateFieldFormats((prev) => prev.map((f, i) => (i === index ? { ...f, ...changes } : f)))
  }

  function removeDateFieldFormat(index) {
    setDateFieldFormats((prev) => prev.filter((_, i) => i !== index))
  }

  function handleSubmit(e) {
    e.preventDefault()
    if (!name.trim()) {
      setError('Name is required.')
      return
    }
    // Commit anything still sitting in the tag box so it isn't silently lost.
    const pending = tagInput.trim()
    const finalTags = pending && !tags.some((t) => t.toLowerCase() === pending.toLowerCase())
      ? [...tags, pending]
      : tags

    const finalIsTemplate = canEditTemplates && isTemplate

    onSave({
      id: initialValues?.id,
      name: name.trim(),
      category: category.trim() || 'Uncategorized',
      tags: finalTags,
      isTemplate: finalIsTemplate,
      description: description.trim() || null,
      // Empty array = shared with everyone.
      sharedWithUsernames: finalIsTemplate && !shareEveryone ? sharedWith : [],
      dateFieldFormats: dateFieldFormats.filter((f) => f.columnName.trim()),
    })
  }

  return (
    <Modal
      visible
      size="large"
      header={isEditing ? 'Edit favorite' : 'Save as favorite'}
      onDismiss={onClose}
      closeAriaLabel="Close favorite dialog"
    >
      <form className="cloudscape-modal-content" onSubmit={handleSubmit}>

        <label>
          Name
          <CloudscapeInput value={name} onChange={(e) => setName(e.target.value)} autoFocus required />
        </label>

        <label>
          Category
          <CloudscapeInput
            value={category}
            onChange={(e) => setCategory(e.target.value)}
            list="existing-categories"
            placeholder="Pick an existing one or type a new name"
          />
          <datalist id="existing-categories">
            {existingCategories.map((c) => <option key={c} value={c} />)}
          </datalist>
        </label>
        {categoryStatus && (
          <p className={`field-hint ${categoryStatus.type === 'new' ? 'hint-new' : 'hint-existing'}`}>
            {categoryStatus.label}
          </p>
        )}

        <label>
          Tags
          <div className="tag-input-wrapper">
            {tags.map((t) => (
              <span key={t} className="tag tag-removable">
                {t}
                <CloudscapeButton type="button" onClick={() => removeTag(t)} aria-label={`Remove tag ${t}`}>×</CloudscapeButton>
              </span>
            ))}
            <CloudscapeInput
              className="tag-input"
              value={tagInput}
              onChange={(e) => { setTagInput(e.target.value); setError(null) }}
              onKeyDown={handleTagKeyDown}
              placeholder={tags.length ? '' : 'Type a tag, press Enter'}
            />
          </div>
        </label>

        {tagSuggestions.length > 0 && (
          <div className="tag-suggestions">
            <span className="muted">Existing:</span>
            {tagSuggestions.map((t) => (
              <CloudscapeButton type="button" key={t} className="tag tag-suggestion" onClick={() => addTag(t)}>
                + {t}
              </CloudscapeButton>
            ))}
          </div>
        )}

        <label>Date fields (optional)</label>
        <p className="field-hint hint-existing">
          If a result column holds a raw date like "20260809220911", define how to read it and
          how it should display - e.g. source "yyyyMMddHHmmss" → display "yyyy/MM/dd HH:mm:ss"
          shows "2026/08/09 22:09:11". This only changes how it's shown in the grid.
        </p>
        {dateFieldFormats.map((f, i) => (
          <div key={i} className="date-format-row">
            <CloudscapeInput placeholder="Column name" value={f.columnName}
                   onChange={(e) => updateDateFieldFormat(i, { columnName: e.target.value })} />
            <CloudscapeInput placeholder="Source format" value={f.sourceFormat}
                   onChange={(e) => updateDateFieldFormat(i, { sourceFormat: e.target.value })} />
            <span className="muted">→</span>
            <CloudscapeInput placeholder="Display format" value={f.displayFormat}
                   onChange={(e) => updateDateFieldFormat(i, { displayFormat: e.target.value })} />
            <CloudscapeButton type="button" className="icon-button" onClick={() => removeDateFieldFormat(i)} aria-label="Remove">×</CloudscapeButton>
          </div>
        ))}
        <CloudscapeButton type="button" className="secondary" onClick={addDateFieldFormat}>+ Add date field</CloudscapeButton>

        {canEditTemplates && (
          <>
            <Checkbox checked={isTemplate} onChange={({ detail }) => setIsTemplate(detail.checked)}>
              Add to the shared Query Templates library
            </Checkbox>
            {isTemplate && (
              <>
                <label>
                  Description (shown to people who don't know SQL)
                  <CloudscapeTextarea rows={2} value={description} onChange={(e) => setDescription(e.target.value)}
                            placeholder="Looks up a customer's orders by their email address." />
                </label>

                <RadioGroup
                  value={shareEveryone ? 'everyone' : 'specific'}
                  onChange={({ detail }) => setShareEveryone(detail.value === 'everyone')}
                  items={[
                    { value: 'everyone', label: 'Everyone' },
                    { value: 'specific', label: 'Specific people' },
                  ]}
                />
                {!shareEveryone && (
                  <UserAutocomplete multi value={sharedWith} onChange={setSharedWith} placeholder="Type a username to share with…" />
                )}
              </>
            )}
          </>
        )}

        {error && <Alert type="error">{error}</Alert>}

        <div className="modal-actions">
          <CloudscapeButton type="button" className="secondary" onClick={onClose}>Cancel</CloudscapeButton>
          <CloudscapeButton type="submit">{isEditing ? 'Save changes' : 'Save'}</CloudscapeButton>
        </div>
      </form>
    </Modal>
  )
}

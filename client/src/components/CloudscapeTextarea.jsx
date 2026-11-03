import { forwardRef } from 'react'
import Textarea from '@cloudscape-design/components/textarea'

const CloudscapeTextarea = forwardRef(function CloudscapeTextarea({
  value,
  defaultValue,
  onChange,
  className,
  rows,
  placeholder,
  disabled,
  ...nativeAttributes
}, ref) {
  return (
    <Textarea
      ref={ref}
      value={value == null ? String(defaultValue ?? '') : String(value)}
      onChange={onChange ? (event) => onChange({ target: { value: event.detail.value } }) : undefined}
      className={className}
      rows={rows}
      placeholder={placeholder}
      disabled={disabled}
      nativeTextareaAttributes={nativeAttributes}
    />
  )
})

export default CloudscapeTextarea

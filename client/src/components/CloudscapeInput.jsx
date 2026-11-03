import Input from '@cloudscape-design/components/input'

export default function CloudscapeInput({
  type = 'text',
  value,
  defaultValue,
  onChange,
  className,
  title,
  'aria-label': ariaLabel,
  autoFocus,
  disabled,
  placeholder,
  ...nativeAttributes
}) {
  return (
    <Input
      type={type}
      value={value == null ? String(defaultValue ?? '') : String(value)}
      onChange={onChange ? (event) => onChange({ target: { value: event.detail.value } }) : undefined}
      className={className}
      ariaLabel={ariaLabel}
      autoFocus={autoFocus}
      disabled={disabled}
      placeholder={placeholder}
      nativeInputAttributes={{ ...nativeAttributes, title }}
    />
  )
}

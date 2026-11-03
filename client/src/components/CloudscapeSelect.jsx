import { Children } from 'react'
import Multiselect from '@cloudscape-design/components/multiselect'
import Select from '@cloudscape-design/components/select'

function optionLabel(children) {
  return Children.toArray(children)
    .map((child) => typeof child === 'string' || typeof child === 'number' ? String(child) : '')
    .join('')
}

export default function CloudscapeSelect({
  children,
  value,
  onChange,
  multiple = false,
  disabled,
  className,
  title,
  'aria-label': ariaLabel,
  ...nativeAttributes
}) {
  const options = Children.toArray(children)
    .filter((child) => child?.type === 'option')
    .map((child) => ({
      value: String(child.props.value ?? ''),
      label: optionLabel(child.props.children),
      disabled: child.props.disabled,
    }))

  if (multiple) {
    const selectedOptions = options.filter((option) => (value || []).map(String).includes(option.value))
    return (
      <Multiselect
        options={options}
        selectedOptions={selectedOptions}
        onChange={onChange ? (event) => onChange({ target: { value: event.detail.selectedOptions.map((option) => option.value) } }) : undefined}
        disabled={disabled}
        className={className}
        ariaLabel={ariaLabel || title}
        {...nativeAttributes}
      />
    )
  }

  const selectedOption = options.find((option) => option.value === String(value ?? '')) || null
  return (
    <Select
      options={options}
      selectedOption={selectedOption}
      onChange={onChange ? (event) => onChange({ target: { value: event.detail.selectedOption?.value ?? '' } }) : undefined}
      disabled={disabled}
      className={className}
      ariaLabel={ariaLabel || title}
      {...nativeAttributes}
    />
  )
}

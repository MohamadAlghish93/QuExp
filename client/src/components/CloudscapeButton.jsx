import Button from '@cloudscape-design/components/button'

export default function CloudscapeButton({
  type = 'button',
  className,
  title,
  onClick,
  'aria-label': ariaLabel,
  variant,
  wrapText = false,
  ...props
}) {
  const resolvedVariant = variant || (className?.split(/\s+/).includes('link-button') ? 'link' : type === 'submit' ? 'primary' : 'normal')

  return (
    <Button
      {...props}
      variant={resolvedVariant}
      wrapText={wrapText}
      className={className}
      ariaLabel={ariaLabel}
      formAction={type === 'submit' ? 'submit' : undefined}
      nativeButtonAttributes={{
        type: type === 'submit' ? 'submit' : 'button',
        title,
        onClick,
      }}
    />
  )
}

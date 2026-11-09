// Thin-line icon set — a small, consistent SVG system that replaces the emoji the
// app used to lean on. Every icon inherits `currentColor` and sizes to `size`
// (default 16), so it takes the colour/weight of whatever button or text wraps it.
// Keep these minimal and professional; no fills, 1.6 stroke, rounded joins.
function Svg({ size = 16, children, label, style, fill = 'none', stroke = 'currentColor', className }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill={fill}
      stroke={stroke} strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" className={className}
      aria-hidden={label ? undefined : true} role={label ? 'img' : undefined} aria-label={label}
      style={{ display: 'inline-block', verticalAlign: '-2px', flex: 'none', ...style }}>
      {children}
    </svg>
  )
}

export const EditIcon = (p) => <Svg {...p}><path d="M12 20h9" /><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z" /></Svg>
export const TrashIcon = (p) => <Svg {...p}><path d="M3 6h18" /><path d="M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2" /><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" /><path d="M10 11v6M14 11v6" /></Svg>
export const KeyIcon = (p) => <Svg {...p}><circle cx="7.5" cy="15.5" r="4.5" /><path d="M10.5 12.5 20 3" /><path d="M16 7l3 3M14 9l2 2" /></Svg>
export const PrintIcon = (p) => <Svg {...p}><path d="M6 9V3h12v6" /><path d="M6 18H4a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-2" /><rect x="6" y="14" width="12" height="7" rx="1" /></Svg>
export const ReceiptIcon = (p) => <Svg {...p}><path d="M5 3v18l2-1 2 1 2-1 2 1 2-1 2 1V3l-2 1-2-1-2 1-2-1-2 1Z" /><path d="M9 8h6M9 12h6" /></Svg>
export const KitchenIcon = (p) => <Svg {...p}><path d="M7 21h10" /><path d="M7 21v-4h10v4" /><path d="M6 13a6 6 0 0 1 12 0Z" /></Svg>
export const PlusIcon = (p) => <Svg {...p}><path d="M12 5v14M5 12h14" /></Svg>
export const ArrowLeftIcon = (p) => <Svg {...p}><path d="M19 12H5" /><path d="m12 19-7-7 7-7" /></Svg>
export const RefreshIcon = (p) => <Svg {...p}><path d="M21 12a9 9 0 1 1-2.64-6.36" /><path d="M21 3v5h-5" /></Svg>
export const SearchIcon = (p) => <Svg {...p}><circle cx="11" cy="11" r="7" /><path d="m21 21-4-4" /></Svg>
export const BellIcon = (p) => <Svg {...p}><path d="M6 9a6 6 0 0 1 12 0c0 5 2 6 2 6H4s2-1 2-6Z" /><path d="M10 20a2 2 0 0 0 4 0" /></Svg>
export const ClockIcon = (p) => <Svg {...p}><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 3" /></Svg>
export const TableIcon = (p) => <Svg {...p}><path d="M3 10h18" /><path d="M5 10V6a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v4" /><path d="M6 10v9M18 10v9" /></Svg>
export const CheckIcon = (p) => <Svg {...p}><path d="M20 6 9 17l-5-5" /></Svg>
export const CashIcon = (p) => <Svg {...p}><rect x="2" y="6" width="20" height="12" rx="2" /><circle cx="12" cy="12" r="2.5" /><path d="M6 12h.01M18 12h.01" /></Svg>
export const QrIcon = (p) => <Svg {...p}><rect x="3" y="3" width="7" height="7" rx="1" /><rect x="14" y="3" width="7" height="7" rx="1" /><rect x="3" y="14" width="7" height="7" rx="1" /><path d="M14 14h3v3M21 14v.01M14 21h.01M17 21h4v-4" /></Svg>
export const PhoneIcon = (p) => <Svg {...p}><rect x="6" y="2" width="12" height="20" rx="2.5" /><path d="M11 18h2" /></Svg>
export const NoteIcon = (p) => <Svg {...p}><path d="M4 4h16v12l-4 4H4Z" /><path d="M16 20v-4h4M8 9h8M8 13h5" /></Svg>
export const CloseIcon = (p) => <Svg {...p}><path d="M6 6l12 12M18 6 6 18" /></Svg>
export const ImageIcon = (p) => <Svg {...p}><rect x="3" y="3" width="18" height="18" rx="2" /><circle cx="8.5" cy="8.5" r="1.5" /><path d="m21 15-5-5L5 21" /></Svg>
export const UserIcon = (p) => <Svg {...p}><circle cx="12" cy="8" r="4" /><path d="M4 21a8 8 0 0 1 16 0" /></Svg>
export const BoxIcon = (p) => <Svg {...p}><path d="m3.3 7 8.7 5 8.7-5" /><path d="M12 22V12" /><path d="M21 16V8a2 2 0 0 0-1-1.7l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.7l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z" /></Svg>
export const ChartIcon = (p) => <Svg {...p}><path d="M3 3v18h18" /><path d="M7 15l4-4 3 3 5-6" /></Svg>
export const TrendIcon = (p) => <Svg {...p}><path d="M3 17l6-6 4 4 8-8" /><path d="M15 7h6v6" /></Svg>
export const CalcIcon = (p) => <Svg {...p}><rect x="4" y="2" width="16" height="20" rx="2" /><path d="M8 6h8M8 10h.01M12 10h.01M16 10h.01M8 14h.01M12 14h.01M16 14v4M8 18h4" /></Svg>
export const BankIcon = (p) => <Svg {...p}><path d="M3 10 12 4l9 6" /><path d="M5 10v8M10 10v8M14 10v8M19 10v8" /><path d="M3 21h18" /></Svg>
export const GearIcon = (p) => <Svg {...p}><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-2.7 1.1V21a2 2 0 1 1-4 0v-.2a1.6 1.6 0 0 0-2.7-1.1l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.6 1.6 0 0 0-1.1-2.7H2a2 2 0 1 1 0-4h.2a1.6 1.6 0 0 0 1.1-2.7l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.6 1.6 0 0 0 1.8.3H8a1.6 1.6 0 0 0 1-1.5V2a2 2 0 1 1 4 0v.2a1.6 1.6 0 0 0 2.7 1.1l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.6 1.6 0 0 0-.3 1.8V8a1.6 1.6 0 0 0 1.5 1H22a2 2 0 1 1 0 4h-.2a1.6 1.6 0 0 0-1.4.9Z" /></Svg>
export const MenuIcon = (p) => <Svg {...p}><path d="M6 3v18" /><path d="M6 8c2 0 2-5 0-5M18 3v7a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2Zm0 7v11" /></Svg>
export const DownloadIcon = (p) => <Svg {...p}><path d="M12 3v12" /><path d="m7 12 5 5 5-5" /><path d="M5 21h14" /></Svg>
export const WarnIcon = (p) => <Svg {...p}><path d="M10.3 3.6 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.6a2 2 0 0 0-3.4 0Z" /><path d="M12 9v4M12 17h.01" /></Svg>
export const StarIcon = (p) => <Svg {...p} fill="currentColor" stroke="none"><path d="M12 2.5l2.9 6 6.6.9-4.8 4.6 1.2 6.5L12 17.9 6.1 21l1.2-6.5L2.5 9.4l6.6-.9Z" /></Svg>
export const ListIcon = (p) => <Svg {...p}><path d="M4 7h16M4 12h16M4 17h16" /></Svg>
export const LogoutIcon = (p) => <Svg {...p}><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" /><path d="m16 17 5-5-5-5" /><path d="M21 12H9" /></Svg>
export const BookIcon = (p) => <Svg {...p}><path d="M4 5a2 2 0 0 1 2-2h14v15H6a2 2 0 0 0-2 2Z" /><path d="M4 20a2 2 0 0 0 2 2h14v-4" /><path d="M9 8h6" /></Svg>
export const UsersIcon = (p) => <Svg {...p}><circle cx="9" cy="8" r="3.5" /><path d="M2.5 21a6.5 6.5 0 0 1 13 0" /><path d="M16 4.6a3.5 3.5 0 0 1 0 6.8M18 15a6.5 6.5 0 0 1 3.5 6" /></Svg>
export const WalletIcon = (p) => <Svg {...p}><path d="M20 7H5a2 2 0 0 1 0-4h13v4" /><path d="M3 5v14a2 2 0 0 0 2 2h15a1 1 0 0 0 1-1V8a1 1 0 0 0-1-1" /><path d="M17 14h.01" /></Svg>
export const BagIcon = (p) => <Svg {...p}><path d="M6 8h12l1.2 12.1a1 1 0 0 1-1 1.1H5.8a1 1 0 0 1-1-1.1Z" /><path d="M9 11V6a3 3 0 0 1 6 0v5" /></Svg>
export const TruckIcon = (p) => <Svg {...p}><circle cx="6" cy="18" r="2.4" /><circle cx="18" cy="18" r="2.4" /><path d="M8.4 18h7.2M3.6 18H2V6h12v12" /><path d="M14 10h4l4 4v4h-1.6" /></Svg>
export const SwapIcon = (p) => <Svg {...p}><path d="M4 8h14" /><path d="m14 4 4 4-4 4" /><path d="M20 16H6" /><path d="m10 12-4 4 4 4" /></Svg>
export const ChatIcon = (p) => <Svg {...p}><path d="M21 12a8.6 8.6 0 0 1-12.7 7.6L3 21l1.5-5A8.6 8.6 0 1 1 21 12Z" /></Svg>
export const EyeIcon = (p) => <Svg {...p}><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7Z" /><circle cx="12" cy="12" r="3" /></Svg>
export const EyeOffIcon = (p) => <Svg {...p}><path d="M3 3l18 18" /><path d="M10.6 5.2A9.7 9.7 0 0 1 12 5c6.5 0 10 7 10 7a17 17 0 0 1-3.2 4M6.6 6.6A17 17 0 0 0 2 12s3.5 7 10 7a9.6 9.6 0 0 0 4-.9" /><path d="M9.9 9.9a3 3 0 0 0 4.2 4.2" /></Svg>

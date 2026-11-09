import React from 'react'
import ReactDOM from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App.jsx'
import '@fontsource-variable/fraunces'
import '@fontsource-variable/dm-sans'
import './styles.css'
import './design/base.css'
import './design/auth.css'
import './design/guest.css'
import './design/staff.css'
import './design/landing.css'

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </React.StrictMode>
)

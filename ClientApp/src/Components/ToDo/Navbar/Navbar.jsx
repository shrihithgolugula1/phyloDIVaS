import React, { useState, useEffect, useRef } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import styles from './Navbar.module.css';
import { logout } from "../../../api/auth";


const Navbar = () => {
  const location = useLocation();
  const navigate = useNavigate();
  const [isLoggedIn, setIsLoggedIn] = useState(false);
  const [indicatorStyle, setIndicatorStyle] = useState({});
  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const navRef = useRef(null);
  
  const navItems = [
    { path: '/Home', label: 'Home' },
    { path: '/About', label: 'About' },
    { path: '/Search', label: 'Explore Data', aliases: ['/GeneSearch'] },
    { path: '/Analysis', label: 'Run Analysis' }
  ];

  // Check login status on mount and when location changes
  useEffect(() => {
    const checkAuthStatus = () => {
      const idToken = sessionStorage.getItem('idToken');
      setIsLoggedIn(!!idToken);
    };
    
    checkAuthStatus();
    setIsMenuOpen(false);
  }, [location]); // Re-check when navigation happens

  useEffect(() => {
    const activeIndex = navItems.findIndex(item => location.pathname === item.path || item.aliases?.includes(location.pathname));
    if (activeIndex !== -1 && navRef.current) {
      const links = navRef.current.querySelectorAll('a');
      const activeLink = links[activeIndex];
      if (activeLink) {
        const containerLeft = navRef.current.getBoundingClientRect().left;
        const linkLeft = activeLink.getBoundingClientRect().left;
        const offset = linkLeft - containerLeft;
        
        setIndicatorStyle({
          width: `${activeLink.offsetWidth}px`,
          transform: `translateX(${offset}px)`
        });
      }
    }
  }, [location.pathname]);

  const handleLogout = async () => {
  try {
    const accessToken = sessionStorage.getItem('accessToken');
    const refreshToken = sessionStorage.getItem('refreshToken');
    
    // Call your logout API to invalidate tokens on the server
    if (accessToken && refreshToken) {
      await logout(accessToken, refreshToken);
    }
  } catch (error) {
    console.error('Logout API error:', error);
    // Continue with client-side logout even if API fails
  } finally {
    // Always clear tokens from sessionStorage
    sessionStorage.removeItem('accessToken');
    sessionStorage.removeItem('idToken');
    sessionStorage.removeItem('refreshToken');
    
    // Update state
    setIsLoggedIn(false);
    
    // Redirect to home or login page
    navigate('/');
  }
};

  return (
    <div className={styles.body}>
      <nav>
        <Link to="/Home" className={styles.logoContainer} aria-label="phyloDIVaS home">
          <img className={styles.logoMark} src="/phylodivas-logo-mark.png" alt="" />
          <span className={styles.wordmark}><span>phylo</span><strong>DIVaS</strong></span>
        </Link>

        <button
          className={styles.menuToggle}
          aria-label="Toggle navigation"
          onClick={() => setIsMenuOpen(prev => !prev)}
        >
          <span />
          <span />
          <span />
        </button>

        <div className={`${styles.centerNavContainer} ${isMenuOpen ? styles.mobileOpen : ''}`}>
          <div className={styles.centerNav} ref={navRef}>
            <div className={styles.navPill} style={indicatorStyle}></div>
            {navItems.map((item) => (
              <Link
                key={item.path}
                to={item.path}
                className={`${styles.navLink} ${location.pathname === item.path || item.aliases?.includes(location.pathname) ? styles.navLinkActive : ''}`}
              >
                {item.label}
              </Link>
            ))}
            <div className={styles.mobileAuth}>
              {isLoggedIn ? (
                <>
                  <Link to="/Profile" className={styles.navLink}>
                    Profile
                  </Link>
                  <button onClick={handleLogout} className={styles.logoutBtn}>
                    Logout
                  </button>
                </>
              ) : (
                <>
                  <Link to="/Login" className={styles.navLink}>
                    Login
                  </Link>
                  <Link to="/Register" className={styles.navLink}>
                    Register
                  </Link>
                </>
              )}
            </div>
          </div>
        </div>
        
        <div className={styles.auth}>
          {isLoggedIn ? (
            <>
              <Link to="/Profile" className={styles.authLink}>
                Profile
              </Link>
              <button onClick={handleLogout} className={styles.logoutBtn}>
                Logout
              </button>
            </>
          ) : (
            <>
              <Link to="/Login" className={styles.authLink}>
                Login
              </Link>
              <Link to="/Register" className={styles.registerBtn}>
                Register
              </Link>
            </>
          )}
        </div>
      </nav>
    </div>
  );
};

export default Navbar;

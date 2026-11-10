import React, { useState, useRef, useEffect } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faSave, faEllipsisVertical } from '@fortawesome/free-solid-svg-icons';
import styles from './ThreeDotMenu.module.css';
import LoginPromptModal from '../Modals/LoginPromptModal/LoginPromptModal';
import SaveSuccessModal from '../Modals/SaveSuccessModal/SaveSuccessModal';

const ThreeDotMenu = ({ onSaveSearch }) => {
  const [isOpen, setIsOpen] = useState(false);
  const [showLoginModal, setShowLoginModal] = useState(false);
  const [showSuccessModal, setShowSuccessModal] = useState(false);
  const menuRef = useRef(null);

  // Close menu when clicking outside
  useEffect(() => {
    const handleClickOutside = (event) => {
      if (menuRef.current && !menuRef.current.contains(event.target)) {
        setIsOpen(false);
      }
    };

    if (isOpen) {
      document.addEventListener('mousedown', handleClickOutside);
    }

    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
    };
  }, [isOpen]);

  const handleSaveSearch = async () => {
    const idToken = sessionStorage.getItem('idToken');
    
    if (!idToken) {
      // User is not logged in, show login prompt modal
      setShowLoginModal(true);
      setIsOpen(false);
    } else {
      // User is logged in, proceed with save
      try {
        await onSaveSearch();
        setIsOpen(false);
        setShowSuccessModal(true); // Show success modal after save
      } catch (error) {
        console.error('Error saving search:', error);
      }
    }
  };

  return (
    <>
      <div className={styles.menuContainer} ref={menuRef}>
        <button
          className={styles.threeDotButton}
          onClick={() => setIsOpen(!isOpen)}
          aria-label="More options"
        >
          <FontAwesomeIcon icon={faEllipsisVertical} />
        </button>

        {isOpen && (
          <div className={styles.menuDropdown}>
            <button className={styles.menuItem} onClick={handleSaveSearch}>
              <span className={styles.menuIcon}><FontAwesomeIcon icon={faSave}/></span>
              Save Search Query
            </button>
            
          </div>
        )}
      </div>

      {/* Login Prompt Modal - shown when user is not logged in */}
      <LoginPromptModal
        isOpen={showLoginModal}
        onClose={() => setShowLoginModal(false)}
        feature="save search queries"
      />

      {/* Success Modal - shown when search is saved successfully */}
      <SaveSuccessModal
        isOpen={showSuccessModal}
        onClose={() => setShowSuccessModal(false)}
      />
    </>
  );
};

export default ThreeDotMenu;

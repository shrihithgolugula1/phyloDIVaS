import React from 'react';
import { useNavigate } from 'react-router-dom';
import Card from '../../Card/Card';
import Button from '../../Button/Button';
import styles from './SaveSuccessModal.module.css';

const SaveSuccessModal = ({ isOpen, onClose }) => {
  const navigate = useNavigate();

  if (!isOpen) return null;

  const handleViewSavedSearches = () => {
    onClose();
    navigate('/Profile', { state: { tab: 'savedSearches' } });
  };

  return (
    <div className={styles.modalOverlay} onClick={onClose}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <button className={styles.closeButton} onClick={onClose} aria-label="Close">
          ×
        </button>
        <Card
          title="Search Saved Successfully!"
          body={
            <div className={styles.modalBody}>
              <p>Your search has been saved and can be accessed anytime from your profile.</p>
            </div>
          }
          footer={
            <div className={styles.modalFooter}>
              <Button
                label="View Saved Searches"
                color="green"
                variant="primary"
                onClick={handleViewSavedSearches}
              />
              <Button
                label="Close"
                color="grey"
                variant="secondary"
                onClick={onClose}
              />
            </div>
          }
        />
      </div>
    </div>
  );
};

export default SaveSuccessModal;
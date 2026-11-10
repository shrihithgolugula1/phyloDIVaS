import React from 'react';
import { useNavigate } from 'react-router-dom';
import Card from '../../Card/Card';
import Button from '../../Button/Button';
import styles from './LoginPromptModal.module.css';

const LoginPromptModal = ({ isOpen, onClose, feature = "save searches" }) => {
  const navigate = useNavigate();

  if (!isOpen) return null;

  const handleLogin = () => {
    onClose();
    navigate('/Login');
  };

  const handleRegister = () => {
    onClose();
    navigate('/Register');
  };

  return (
    <div className={styles.modalOverlay} onClick={onClose}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <Card
          title="Login Required"
          body={
            <div className={styles.modalBody}>
              <p>You need to be logged in to {feature}.</p>
              <p>Please log in or create an account to continue.</p>
            </div>
          }
          footer={
            <div className={styles.modalFooter}>
              <Button
                label="Login"
                color="green"
                variant="primary"
                onClick={handleLogin}
              />
              <Button
                label="Register"
                color="grey"
                variant="secondary"
                onClick={handleRegister}
              />
              <Button
                label="Cancel"
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

export default LoginPromptModal;
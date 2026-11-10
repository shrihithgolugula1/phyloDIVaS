import { useState } from 'react';
import { MemoryRouter } from 'react-router-dom';
import LoginPromptModal from '../../../Components/Modals/LoginPromptModal/LoginPromptModal';
import Button from '../../../Components/Button/Button';

export default {
  title: 'Components/Modals/LoginPromptModal',
  component: LoginPromptModal,
  decorators: [
    (Story) => (
      <MemoryRouter>
        <Story />
      </MemoryRouter>
    ),
  ],
  argTypes: {
    isOpen: {
      control: 'boolean',
      description: 'Controls modal visibility',
    },
    feature: {
      control: 'text',
      description: 'The feature name that requires login',
    },
    onClose: {
      action: 'closed',
      description: 'Callback when modal is closed',
    },
  },
};

// Default Modal
export const Default = {
  render: () => {
    const [isOpen, setIsOpen] = useState(false);

    return (
      <div>
        <Button
          label="Open Login Prompt"
          color="blue"
          variant="primary"
          onClick={() => setIsOpen(true)}
        />
        <LoginPromptModal
          isOpen={isOpen}
          onClose={() => setIsOpen(false)}
        />
      </div>
    );
  },
};

// Custom Feature Name
export const CustomFeature = {
  render: () => {
    const [isOpen, setIsOpen] = useState(false);

    return (
      <div>
        <Button
          label="Open Login Prompt"
          color="blue"
          variant="primary"
          onClick={() => setIsOpen(true)}
        />
        <LoginPromptModal
          isOpen={isOpen}
          onClose={() => setIsOpen(false)}
          feature="access premium content"
        />
      </div>
    );
  },
};

// Different Feature Examples
export const DifferentFeatures = {
  render: () => {
    const [activeModal, setActiveModal] = useState(null);

    const features = [
      { name: 'save searches', label: 'Save Searches' },
      { name: 'create a playlist', label: 'Create Playlist' },
      { name: 'download results', label: 'Download Results' },
      { name: 'share your work', label: 'Share Work' },
      { name: 'leave a comment', label: 'Leave Comment' },
    ];

    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
        <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>
          Click any button to see the login prompt for that feature:
        </p>
        {features.map((feature) => (
          <div key={feature.name}>
            <Button
              label={`Try to ${feature.label}`}
              color="blue"
              variant="primary"
              onClick={() => setActiveModal(feature.name)}
            />
            <LoginPromptModal
              isOpen={activeModal === feature.name}
              onClose={() => setActiveModal(null)}
              feature={feature.name}
            />
          </div>
        ))}
      </div>
    );
  },
};

// Always Open (for design review)
export const AlwaysOpen = {
  args: {
    isOpen: true,
    feature: 'save searches',
    onClose: () => {},
  },
};

// With Animation Demo
export const AnimationDemo = {
  render: () => {
    const [isOpen, setIsOpen] = useState(false);
    const [showMessage, setShowMessage] = useState(false);

    const handleOpen = () => {
      setIsOpen(true);
      setShowMessage(false);
    };

    const handleClose = () => {
      setIsOpen(false);
      setShowMessage(true);
      setTimeout(() => setShowMessage(false), 3000);
    };

    return (
      <div>
        <Button
          label="Open Modal (Watch Animation)"
          color="blue"
          variant="primary"
          onClick={handleOpen}
        />
        {showMessage && (
          <p style={{ marginTop: '1rem', color: '#15ca24', fontWeight: '600' }}>
            ✓ Modal closed successfully
          </p>
        )}
        <LoginPromptModal
          isOpen={isOpen}
          onClose={handleClose}
          feature="save your preferences"
        />
        <div style={{ marginTop: '2rem', padding: '1rem', background: '#f5f5f5', borderRadius: '8px' }}>
          <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>Animation features:</p>
          <ul style={{ paddingLeft: '1.5rem', lineHeight: '1.8' }}>
            <li>Smooth slide-in from top with fade</li>
            <li>Backdrop fade-in effect</li>
            <li>Click backdrop or Cancel to close</li>
          </ul>
        </div>
      </div>
    );
  },
};

// Interaction Patterns
export const InteractionPatterns = {
  render: () => {
    const [isOpen, setIsOpen] = useState(false);
    const [action, setAction] = useState('');

    const handleAction = (actionType) => {
      setAction(`${actionType} button clicked`);
      setIsOpen(false);
      setTimeout(() => setAction(''), 3000);
    };

    return (
      <div>
        <Button
          label="Test Modal Interactions"
          color="blue"
          variant="primary"
          onClick={() => setIsOpen(true)}
        />
        
        {action && (
          <div style={{ 
            marginTop: '1rem', 
            padding: '1rem', 
            background: '#e5ffe7', 
            borderRadius: '8px',
            color: '#063c14',
            fontWeight: '600'
          }}>
            {action}
          </div>
        )}

        <LoginPromptModal
          isOpen={isOpen}
          onClose={() => handleAction('Cancel or backdrop')}
          feature="test this feature"
        />

        <div style={{ marginTop: '2rem', padding: '1rem', background: '#f5f5f5', borderRadius: '8px' }}>
          <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>Ways to close the modal:</p>
          <ul style={{ paddingLeft: '1.5rem', lineHeight: '1.8' }}>
            <li>Click the backdrop (dark overlay)</li>
            <li>Click the "Cancel" button</li>
            <li>Click "Login" or "Register" (navigates away)</li>
          </ul>
        </div>
      </div>
    );
  },
};
import { useState } from 'react';
import { MemoryRouter } from 'react-router-dom';
import SaveSuccessModal from '../../../Components/Modals/SaveSuccessModal/SaveSuccessModal';
import Button from '../../../Components/Button/Button';

export default {
  title: 'Components/Modals/SaveSuccessModal',
  component: SaveSuccessModal,
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
          label="Save Search"
          color="green"
          variant="primary"
          onClick={() => setIsOpen(true)}
        />
        <SaveSuccessModal
          isOpen={isOpen}
          onClose={() => setIsOpen(false)}
        />
      </div>
    );
  },
};

// Always Open (for design review)
export const AlwaysOpen = {
  args: {
    isOpen: true,
    onClose: () => {},
  },
};

// With Animation Demo
export const AnimationDemo = {
  render: () => {
    const [isOpen, setIsOpen] = useState(false);
    const [saveCount, setSaveCount] = useState(0);

    const handleSave = () => {
      setIsOpen(true);
      setSaveCount(saveCount + 1);
    };

    const handleClose = () => {
      setIsOpen(false);
    };

    return (
      <div>
        <Button
          label="Save Search (Watch Animation)"
          color="green"
          variant="primary"
          onClick={handleSave}
        />
        <p style={{ marginTop: '1rem', color: '#666' }}>
          Searches saved: <strong>{saveCount}</strong>
        </p>
        <SaveSuccessModal
          isOpen={isOpen}
          onClose={handleClose}
        />
        <div style={{ marginTop: '2rem', padding: '1rem', background: '#f5f5f5', borderRadius: '8px' }}>
          <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>Animation features:</p>
          <ul style={{ paddingLeft: '1.5rem', lineHeight: '1.8' }}>
            <li>Smooth slide-in from top with fade</li>
            <li>Backdrop fade-in effect</li>
            <li>Success-themed green button</li>
            <li>Close button (×) in top-right corner</li>
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

    const handleSave = () => {
      setIsOpen(true);
      setAction('');
    };

    const handleAction = (actionType) => {
      setAction(actionType);
      setIsOpen(false);
      setTimeout(() => setAction(''), 3000);
    };

    return (
      <div>
        <Button
          label="Save Search"
          color="green"
          variant="primary"
          onClick={handleSave}
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
            Action taken: {action}
          </div>
        )}

        <SaveSuccessModal
          isOpen={isOpen}
          onClose={() => handleAction('Modal closed')}
        />

        <div style={{ marginTop: '2rem', padding: '1rem', background: '#f5f5f5', borderRadius: '8px' }}>
          <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>Ways to close the modal:</p>
          <ul style={{ paddingLeft: '1.5rem', lineHeight: '1.8' }}>
            <li>Click the backdrop (dark overlay)</li>
            <li>Click the × button in top-right</li>
            <li>Click the "Close" button</li>
            <li>Click "View Saved Searches" (navigates to profile)</li>
          </ul>
        </div>
      </div>
    );
  },
};

// Success Flow Demo
export const SuccessFlow = {
  render: () => {
    const [step, setStep] = useState('ready');
    const [isOpen, setIsOpen] = useState(false);

    const handleSave = () => {
      setStep('saving');
      setTimeout(() => {
        setStep('success');
        setIsOpen(true);
      }, 1000);
    };

    const handleClose = () => {
      setIsOpen(false);
      setStep('ready');
    };

    return (
      <div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem', marginBottom: '2rem' }}>
          <Button
            label={step === 'saving' ? 'Saving...' : 'Save Search'}
            color="green"
            variant="primary"
            onClick={handleSave}
            disabled={step === 'saving'}
          />
          
          {step === 'saving' && (
            <p style={{ color: '#666', fontStyle: 'italic' }}>
              Processing your request...
            </p>
          )}
        </div>

        <SaveSuccessModal
          isOpen={isOpen}
          onClose={handleClose}
        />

        <div style={{ padding: '1rem', background: '#f5f5f5', borderRadius: '8px' }}>
          <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>Typical user flow:</p>
          <ol style={{ paddingLeft: '1.5rem', lineHeight: '1.8' }}>
            <li>User clicks "Save Search"</li>
            <li>Loading state shows briefly</li>
            <li>Success modal appears after save completes</li>
            <li>User can view saved searches or close</li>
          </ol>
        </div>
      </div>
    );
  },
};


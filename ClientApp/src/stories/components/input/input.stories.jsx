import { useState } from 'react';
import Input from '../../../Components/Forms/Input/Input';
import 'bootstrap/dist/css/bootstrap.min.css';

export default {
  title: 'Components/Input',
  component: Input,
  argTypes: {
    type: {
      control: 'select',
      options: ['text', 'email', 'password', 'number', 'tel', 'url'],
      description: 'Input type',
    },
    placeholder: {
      control: 'text',
      description: 'Placeholder text',
    },
    name: {
      control: 'text',
      description: 'Input name attribute',
    },
    variant: {
      control: 'select',
      options: ['primary', 'success', 'error'],
      description: 'Input style variant',
    },
    value: {
      control: 'text',
      description: 'Controlled input value',
    },
    readOnly: {
      control: 'boolean',
      description: 'Makes input read-only',
    },
    disabled: {
      control: 'boolean',
      description: 'Disables the input',
    },
    autoComplete: {
      control: 'text',
      description: 'Autocomplete attribute',
    },
  },
};

// Default Input
export const Default = {
  args: {
    type: 'text',
    placeholder: 'Enter text...',
    name: 'default-input',
  },
};

// Primary Variant
export const Primary = {
  args: {
    type: 'text',
    placeholder: 'Primary input',
    name: 'primary-input',
    variant: 'primary',
  },
};

// Success Variant
export const Success = {
  args: {
    type: 'text',
    placeholder: 'Success input',
    name: 'success-input',
    variant: 'success',
    value: 'Valid input',
  },
};

// Error Variant
export const Error = {
  args: {
    type: 'text',
    placeholder: 'Error input',
    name: 'error-input',
    variant: 'error',
    value: 'Invalid input',
  },
};

// All Variants
export const AllVariants = {
  render: () => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem', maxWidth: '400px' }}>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Default
        </label>
        <Input type="text" placeholder="Default input" name="default" />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Primary (with focus state)
        </label>
        <Input type="text" placeholder="Primary input" name="primary" variant="primary" />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Success
        </label>
        <Input type="text" placeholder="Success input" name="success" variant="success" value="Valid entry" />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Error
        </label>
        <Input type="text" placeholder="Error input" name="error" variant="error" value="Invalid entry" />
      </div>
    </div>
  ),
};

// Different Input Types
export const InputTypes = {
  render: () => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem', maxWidth: '400px' }}>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Text
        </label>
        <Input type="text" placeholder="Enter text" name="text-input" />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Email
        </label>
        <Input type="email" placeholder="Enter email" name="email-input" autoComplete="email" />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Password
        </label>
        <Input type="password" placeholder="Enter password" name="password-input" autoComplete="current-password" />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Number
        </label>
        <Input type="number" placeholder="Enter number" name="number-input" />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Telephone
        </label>
        <Input type="tel" placeholder="Enter phone number" name="tel-input" autoComplete="tel" />
      </div>
    </div>
  ),
};

// Read Only
export const ReadOnly = {
  args: {
    type: 'text',
    placeholder: 'Read only input',
    name: 'readonly-input',
    value: 'This cannot be edited',
    readOnly: true,
  },
};

// Disabled
export const Disabled = {
  args: {
    type: 'text',
    placeholder: 'Disabled input',
    name: 'disabled-input',
    value: 'This is disabled',
    disabled: true,
  },
};

// Controlled Input
export const Controlled = {
  render: () => {
    const [value, setValue] = useState('');
    
    return (
      <div style={{ maxWidth: '400px' }}>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Type something:
        </label>
        <Input
          type="text"
          placeholder="Start typing..."
          name="controlled-input"
          value={value}
          onChange={(e) => setValue(e.target.value)}
        />
        <p style={{ marginTop: '1rem', color: '#666' }}>
          Current value: <strong>{value || '(empty)'}</strong>
        </p>
      </div>
    );
  },
};

// Form Example
export const InFormContext = {
  render: () => {
    const [formData, setFormData] = useState({
      name: '',
      email: '',
      phone: '',
    });

    const handleChange = (e) => {
      setFormData({
        ...formData,
        [e.target.name]: e.target.value,
      });
    };

    return (
      <form style={{ maxWidth: '400px', display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
        <div>
          <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
            Name
          </label>
          <Input
            type="text"
            placeholder="Enter your name"
            name="name"
            value={formData.name}
            onChange={handleChange}
            variant="primary"
          />
        </div>
        <div>
          <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
            Email
          </label>
          <Input
            type="email"
            placeholder="Enter your email"
            name="email"
            value={formData.email}
            onChange={handleChange}
            variant="primary"
            autoComplete="email"
          />
        </div>
        <div>
          <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
            Phone
          </label>
          <Input
            type="tel"
            placeholder="Enter your phone"
            name="phone"
            value={formData.phone}
            onChange={handleChange}
            variant="primary"
            autoComplete="tel"
          />
        </div>
        <div style={{ padding: '1rem', background: '#f5f5f5', borderRadius: '8px' }}>
          <strong>Form Data:</strong>
          <pre style={{ marginTop: '0.5rem', fontSize: '0.875rem' }}>
            {JSON.stringify(formData, null, 2)}
          </pre>
        </div>
      </form>
    );
  },
};

// Focus States Demo
export const FocusStatesDemo = {
  render: () => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem', maxWidth: '400px' }}>
      <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>
        Click on each input to see the focus effects:
      </p>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', color: '#666' }}>
          Default focus (no variant)
        </label>
        <Input type="text" placeholder="Click to focus" name="focus-default" />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', color: '#666' }}>
          Primary focus (green border & outline)
        </label>
        <Input type="text" placeholder="Click to focus" name="focus-primary" variant="primary" />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', color: '#666' }}>
          Success focus (green background maintained)
        </label>
        <Input type="text" placeholder="Click to focus" name="focus-success" variant="success" />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', color: '#666' }}>
          Error focus (pink border & outline)
        </label>
        <Input type="text" placeholder="Click to focus" name="focus-error" variant="error" />
      </div>
    </div>
  ),
};
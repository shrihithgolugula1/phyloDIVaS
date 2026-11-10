import Button from '../../../Components/Button/Button';
import { MemoryRouter } from 'react-router-dom';

export default {
  title: 'Components/Button',
  component: Button,
  decorators: [
    (Story) => (
      <MemoryRouter>
        <Story />
      </MemoryRouter>
    ),
  ],
  argTypes: {
    label: {
      control: 'text',
      description: 'Button text',
    },
    color: {
      control: 'select',
      options: ['blue', 'green', 'pink', 'grey'],
      description: 'Button color',
    },
    variant: {
      control: 'select',
      options: ['primary', 'secondary'],
      description: 'Button style variant',
    },
    type: {
      control: 'select',
      options: ['button', 'submit', 'reset'],
      description: 'Button HTML type',
    },
    fullWidth: {
      control: 'boolean',
      description: 'Makes button full width',
    },
    linkTo: {
      control: 'text',
      description: 'Route to navigate to (makes it a Link)',
    },
    onClick: {
      action: 'clicked',
      description: 'Click handler function',
    },
  },
};

// Primary Button
export const Primary = {
  args: {
    label: 'Primary Button',
    color: 'blue',
    variant: 'primary',
  },
};

// Secondary Button
export const Secondary = {
  args: {
    label: 'Secondary Button',
    color: 'blue',
    variant: 'secondary',
  },
};

// Button as Link
export const AsLink = {
  args: {
    label: 'Navigate to Home',
    linkTo: '/',
    color: 'blue',
    variant: 'primary',
  },
};

// Color Variants
export const AllColorsPrimary = {
  render: () => (
    <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap' }}>
      <Button label="Blue" color="blue" variant="primary" />
      <Button label="Green" color="green" variant="primary" />
      <Button label="Pink" color="pink" variant="primary" />
      <Button label="Grey" color="grey" variant="primary" />
    </div>
  ),
};

// Secondary Color Variants
export const AllColorsSecondary = {
  render: () => (
    <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap' }}>
      <Button label="Blue" color="blue" variant="secondary" />
      <Button label="Green" color="green" variant="secondary" />
      <Button label="Pink" color="pink" variant="secondary" />
      <Button label="Grey" color="grey" variant="secondary" />
    </div>
  ),
};

// All Variants Comparison
export const AllVariantsComparison = {
  render: () => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '2rem' }}>
      <div>
        <h3 style={{ marginBottom: '1rem', color: '#2C2C2C' }}>Blue</h3>
        <div style={{ display: 'flex', gap: '1rem' }}>
          <Button label="Primary" variant="primary" color="blue" />
          <Button label="Secondary" variant="secondary" color="blue" />
        </div>
      </div>
      <div>
        <h3 style={{ marginBottom: '1rem', color: '#2C2C2C' }}>Green</h3>
        <div style={{ display: 'flex', gap: '1rem' }}>
          <Button label="Primary" variant="primary" color="green" />
          <Button label="Secondary" variant="secondary" color="green" />
        </div>
      </div>
      <div>
        <h3 style={{ marginBottom: '1rem', color: '#2C2C2C' }}>Pink</h3>
        <div style={{ display: 'flex', gap: '1rem' }}>
          <Button label="Primary" variant="primary" color="pink" />
          <Button label="Secondary" variant="secondary" color="pink" />
        </div>
      </div>
      <div>
        <h3 style={{ marginBottom: '1rem', color: '#2C2C2C' }}>Grey</h3>
        <div style={{ display: 'flex', gap: '1rem' }}>
          <Button label="Primary" variant="primary" color="grey" />
          <Button label="Secondary" variant="secondary" color="grey" />
        </div>
      </div>
    </div>
  ),
};

// Full Width
export const FullWidth = {
  args: {
    label: 'Full Width Button',
    color: 'blue',
    variant: 'primary',
    fullWidth: true,
  },
};

// With onClick Handler
export const WithClickHandler = {
  args: {
    label: 'Click Me',
    color: 'green',
    variant: 'primary',
    onClick: () => alert('Button clicked!'),
  },
};

// Interactive States Demo
export const InteractiveStates = {
  render: () => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
      <div>
        <p style={{ marginBottom: '0.5rem', fontWeight: '600' }}>Hover over these buttons to see the hover effects:</p>
        <div style={{ display: 'flex', gap: '1rem' }}>
          <Button label="Hover Me" variant="primary" color="blue" />
          <Button label="Hover Me" variant="secondary" color="pink" />
        </div>
      </div>
      <div>
        <p style={{ marginBottom: '0.5rem', fontWeight: '600' }}>Click and hold to see focus states:</p>
        <div style={{ display: 'flex', gap: '1rem' }}>
          <Button label="Focus Me" variant="primary" color="green" onClick={() => {}} />
          <Button label="Focus Me" variant="secondary" color="blue" onClick={() => {}} />
        </div>
      </div>
    </div>
  ),
};
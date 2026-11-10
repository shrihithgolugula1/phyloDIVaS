import { useState } from 'react';
import Dropdown from '../../../Components/Forms/Dropdown/Dropdown';
import 'bootstrap/dist/css/bootstrap.min.css';

export default {
  title: 'Components/Dropdown',
  component: Dropdown,
  argTypes: {
    name: {
      control: 'text',
      description: 'Dropdown name attribute',
    },
    placeholder: {
      control: 'text',
      description: 'Placeholder text shown when no option is selected',
    },
    variant: {
      control: 'select',
      options: ['primary', 'success', 'error'],
      description: 'Dropdown style variant',
    },
    options: {
      control: 'object',
      description: 'Array of options (can be strings or {label, value} objects)',
    },
    value: {
      control: 'text',
      description: 'Selected value',
    },
    required: {
      control: 'boolean',
      description: 'Makes dropdown required',
    },
  },
};

// Simple string options
const simpleOptions = ['Option 1', 'Option 2', 'Option 3', 'Option 4'];

// Object options with label/value
const objectOptions = [
  { label: 'United States', value: 'us' },
  { label: 'Canada', value: 'ca' },
  { label: 'United Kingdom', value: 'uk' },
  { label: 'Australia', value: 'au' },
];

// Default Dropdown
export const Default = {
  args: {
    name: 'default-dropdown',
    placeholder: 'Select an option',
    options: simpleOptions,
  },
};

// Primary Variant
export const Primary = {
  args: {
    name: 'primary-dropdown',
    placeholder: 'Select an option',
    variant: 'primary',
    options: simpleOptions,
  },
};

// Success Variant
export const Success = {
  args: {
    name: 'success-dropdown',
    placeholder: 'Select an option',
    variant: 'success',
    options: simpleOptions,
    value: 'Option 2',
  },
};

// Error Variant
export const Error = {
  args: {
    name: 'error-dropdown',
    placeholder: 'Select an option',
    variant: 'error',
    options: simpleOptions,
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
        <Dropdown
          name="default"
          placeholder="Select an option"
          options={simpleOptions}
        />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Primary (with focus state)
        </label>
        <Dropdown
          name="primary"
          placeholder="Select an option"
          variant="primary"
          options={simpleOptions}
        />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Success
        </label>
        <Dropdown
          name="success"
          placeholder="Select an option"
          variant="success"
          options={simpleOptions}
          value="Option 1"
        />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Error
        </label>
        <Dropdown
          name="error"
          placeholder="Please select a valid option"
          variant="error"
          options={simpleOptions}
        />
      </div>
    </div>
  ),
};

// With Object Options
export const WithObjectOptions = {
  render: () => {
    const [selected, setSelected] = useState('');

    return (
      <div style={{ maxWidth: '400px' }}>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Select Country
        </label>
        <Dropdown
          name="country"
          placeholder="Choose a country"
          options={objectOptions}
          value={selected}
          onChange={(e) => setSelected(e.target.value)}
          variant="primary"
        />
        <p style={{ marginTop: '1rem', color: '#666' }}>
          Selected value: <strong>{selected || '(none)'}</strong>
        </p>
      </div>
    );
  },
};

// Controlled Dropdown
export const Controlled = {
  render: () => {
    const [value, setValue] = useState('');

    return (
      <div style={{ maxWidth: '400px' }}>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Select an option:
        </label>
        <Dropdown
          name="controlled-dropdown"
          placeholder="Make a selection"
          options={simpleOptions}
          value={value}
          onChange={(e) => setValue(e.target.value)}
        />
        <p style={{ marginTop: '1rem', color: '#666' }}>
          Current value: <strong>{value || '(none selected)'}</strong>
        </p>
        <button
          onClick={() => setValue('')}
          style={{
            marginTop: '0.5rem',
            padding: '0.5rem 1rem',
            borderRadius: '0.5rem',
            border: '1px solid #ccc',
            background: '#f5f5f5',
            cursor: 'pointer',
          }}
        >
          Reset Selection
        </button>
      </div>
    );
  },
};

// Required Dropdown
export const Required = {
  render: () => {
    const [submitted, setSubmitted] = useState(false);

    const handleSubmit = (e) => {
      e.preventDefault();
      setSubmitted(true);
      setTimeout(() => setSubmitted(false), 2000);
    };

    return (
      <form onSubmit={handleSubmit} style={{ maxWidth: '400px' }}>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Select an option <span style={{ color: 'red' }}>*</span>
        </label>
        <Dropdown
          name="required-dropdown"
          placeholder="This field is required"
          options={simpleOptions}
          required={true}
          variant="primary"
        />
        <button
          type="submit"
          style={{
            marginTop: '1rem',
            padding: '0.5rem 1.5rem',
            borderRadius: '50px',
            border: '2px solid #1e61e8',
            background: '#1e61e8',
            color: 'white',
            fontWeight: 'bold',
            cursor: 'pointer',
          }}
        >
          Submit
        </button>
        {submitted && (
          <p style={{ marginTop: '1rem', color: '#15ca24', fontWeight: '600' }}>
            ✓ Form submitted successfully!
          </p>
        )}
      </form>
    );
  },
};

// Form Example
export const InFormContext = {
  render: () => {
    const [formData, setFormData] = useState({
      category: '',
      priority: '',
      status: '',
    });

    const handleChange = (e) => {
      setFormData({
        ...formData,
        [e.target.name]: e.target.value,
      });
    };

    const categories = ['Bug', 'Feature', 'Enhancement', 'Documentation'];
    const priorities = [
      { label: 'Low Priority', value: 'low' },
      { label: 'Medium Priority', value: 'medium' },
      { label: 'High Priority', value: 'high' },
      { label: 'Critical', value: 'critical' },
    ];
    const statuses = ['Open', 'In Progress', 'Review', 'Closed'];

    return (
      <form style={{ maxWidth: '400px', display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
        <div>
          <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
            Category
          </label>
          <Dropdown
            name="category"
            placeholder="Select category"
            options={categories}
            value={formData.category}
            onChange={handleChange}
            variant="primary"
          />
        </div>
        <div>
          <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
            Priority
          </label>
          <Dropdown
            name="priority"
            placeholder="Select priority"
            options={priorities}
            value={formData.priority}
            onChange={handleChange}
            variant="primary"
          />
        </div>
        <div>
          <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
            Status
          </label>
          <Dropdown
            name="status"
            placeholder="Select status"
            options={statuses}
            value={formData.status}
            onChange={handleChange}
            variant="primary"
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
        Click on each dropdown to see the focus effects:
      </p>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', color: '#666' }}>
          Default focus (no variant)
        </label>
        <Dropdown
          name="focus-default"
          placeholder="Click to focus"
          options={simpleOptions}
        />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', color: '#666' }}>
          Primary focus (green border & outline)
        </label>
        <Dropdown
          name="focus-primary"
          placeholder="Click to focus"
          options={simpleOptions}
          variant="primary"
        />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', color: '#666' }}>
          Success focus (green background maintained)
        </label>
        <Dropdown
          name="focus-success"
          placeholder="Click to focus"
          options={simpleOptions}
          variant="success"
        />
      </div>
      <div>
        <label style={{ display: 'block', marginBottom: '0.5rem', color: '#666' }}>
          Error focus (pink border & outline)
        </label>
        <Dropdown
          name="focus-error"
          placeholder="Click to focus"
          options={simpleOptions}
          variant="error"
        />
      </div>
    </div>
  ),
};

// Many Options
export const WithManyOptions = {
  render: () => {
    const countries = [
      'United States', 'Canada', 'Mexico', 'United Kingdom', 'France',
      'Germany', 'Italy', 'Spain', 'Netherlands', 'Belgium',
      'Switzerland', 'Austria', 'Sweden', 'Norway', 'Denmark',
      'Finland', 'Poland', 'Czech Republic', 'Hungary', 'Portugal',
      'Greece', 'Turkey', 'Russia', 'Ukraine', 'Romania',
      'Australia', 'New Zealand', 'Japan', 'China', 'India',
      'Brazil', 'Argentina', 'Chile', 'Colombia', 'Peru',
    ];

    return (
      <div style={{ maxWidth: '400px' }}>
        <label style={{ display: 'block', marginBottom: '0.5rem', fontWeight: '600' }}>
          Select Country
        </label>
        <Dropdown
          name="country-long"
          placeholder="Choose a country"
          options={countries}
          variant="primary"
        />
        <p style={{ marginTop: '0.5rem', color: '#666', fontSize: '0.875rem' }}>
          Dropdown with many options is scrollable
        </p>
      </div>
    );
  },
};
import Card from '../../../Components/Card/Card';
import Button from '../../../Components/Button/Button';
import { MemoryRouter } from 'react-router-dom';

export default {
  title: 'Components/Card',
  component: Card,
  decorators: [
    (Story) => (
      <MemoryRouter>
        <div style={{ padding: '2rem', display: 'flex', justifyContent: 'center' }}>
          <Story />
        </div>
      </MemoryRouter>
    ),
  ],
  argTypes: {
    title: {
      control: 'text',
      description: 'Card title',
    },
    body: {
      control: 'text',
      description: 'Card body content',
    },
    footer: {
      control: 'text',
      description: 'Card footer content',
    },
    icon: {
      control: false,
      description: 'Icon element to display',
    },
  },
};

// Basic Card
export const Basic = {
  args: {
    title: 'Card Title',
    body: 'This is the card body content. It can contain any text or information you want to display.',
  },
};

// Card with Footer
export const WithFooter = {
  args: {
    title: 'Card with Footer',
    body: 'This card includes footer content below the main body.',
    footer: 'Footer information here',
  },
};

// Card with Icon
export const WithIcon = {
  args: {
    title: 'Card with Icon',
    body: 'This card displays an icon at the top.',
    icon: '🎉',
  },
};

// Complete Card
export const Complete = {
  args: {
    title: 'Complete Card',
    body: 'This card has all sections: icon, title, body, and footer.',
    footer: 'Last updated: Today',
    icon: '⭐',
  },
};

// Card with Custom Content
export const WithCustomBody = {
  render: () => (
    <Card
      title="Custom Content Card"
      body={
        <div>
          <p>This card contains custom JSX in the body.</p>
          <ul style={{ paddingLeft: '1.5rem' }}>
            <li>Feature 1</li>
            <li>Feature 2</li>
            <li>Feature 3</li>
          </ul>
        </div>
      }
    />
  ),
};

// Card with Button Footer
export const WithButtonFooter = {
  render: () => (
    <Card
      title="Action Card"
      body="This card has action buttons in the footer."
      footer={
        <div style={{ display: 'flex', gap: '0.5rem', justifyContent: 'center' }}>
          <Button label="Cancel" variant="secondary" color="grey" onClick={() => {}} />
          <Button label="Confirm" variant="primary" color="blue" onClick={() => {}} />
        </div>
      }
    />
  ),
};

// Minimal Card (Title Only)
export const TitleOnly = {
  args: {
    title: 'Simple Card',
  },
};

// Body Only
export const BodyOnly = {
  args: {
    body: 'This card only has body content, no title or footer.',
  },
};
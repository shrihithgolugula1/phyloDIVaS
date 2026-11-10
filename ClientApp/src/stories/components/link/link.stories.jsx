import LinkTo from '../../../Components/Link/Link';
import { MemoryRouter } from 'react-router-dom';

export default {
  title: 'Components/LinkTo',
  component: LinkTo,
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
      description: 'Link text',
    },
    linkTo: {
      control: 'text',
      description: 'Route to navigate to',
    },
    color: {
      control: 'select',
      options: ['blue', 'green', 'pink', 'grey'],
      description: 'Link color',
    },
  },
};

// Default Link
export const Default = {
  args: {
    label: 'Click here',
    linkTo: '/',
    color: 'blue',
  },
};

// Blue Link
export const Blue = {
  args: {
    label: 'Blue Link',
    linkTo: '/about',
    color: 'blue',
  },
};

// Pink Link
export const Pink = {
  args: {
    label: 'Pink Link',
    linkTo: '/contact',
    color: 'pink',
  },
};

// Green Link
export const Green = {
  args: {
    label: 'Green Link',
    linkTo: '/success',
    color: 'green',
  },
};

// Grey Link
export const Grey = {
  args: {
    label: 'Grey Link',
    linkTo: '/info',
    color: 'grey',
  },
};

// All Colors
export const AllColors = {
  render: () => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
      <LinkTo label="Blue Link" linkTo="/" color="blue" />
      <LinkTo label="Green Link" linkTo="/" color="green" />
      <LinkTo label="Pink Link" linkTo="/" color="pink" />
      <LinkTo label="Grey Link" linkTo="/" color="grey" />
    </div>
  ),
};

// In Context
export const InParagraph = {
  render: () => (
    <p style={{ maxWidth: '600px', lineHeight: '1.6' }}>
      This is a paragraph of text that contains a{' '}
      <LinkTo label="link" linkTo="/example" color="blue" /> within it. You can
      use links inline with other content to create natural navigation flows in
      your application.
    </p>
  ),
};

// Navigation Menu
export const NavigationMenu = {
  render: () => (
    <nav style={{ display: 'flex', gap: '2rem', padding: '1rem', background: '#f5f5f5', borderRadius: '8px' }}>
      <LinkTo label="Home" linkTo="/" color="blue" />
      <LinkTo label="About" linkTo="/about" color="blue" />
      <LinkTo label="Services" linkTo="/services" color="blue" />
      <LinkTo label="Contact" linkTo="/contact" color="blue" />
    </nav>
  ),
};

// Hover Effects Demo
export const HoverEffectsDemo = {
  render: () => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
      <p style={{ fontWeight: '600', marginBottom: '0.5rem' }}>
        Hover over these links to see the underline effect:
      </p>
      <div style={{ display: 'flex', gap: '2rem' }}>
        <LinkTo label="Hover over me" linkTo="/" color="blue" />
        <LinkTo label="And me too" linkTo="/" color="pink" />
        <LinkTo label="Don't forget me" linkTo="/" color="green" />
      </div>
    </div>
  ),
};
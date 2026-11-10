import { useState } from 'react';
import SearchBar from '../../../Components/SearchBar/SearchBar';

export default {
  title: 'Components/SearchBar',
  component: SearchBar,
  argTypes: {
    placeholder: {
      control: 'text',
      description: 'Placeholder text for the search input',
    },
    name: {
      control: 'text',
      description: 'Name attribute for the input',
    },
    initialValue: {
      control: 'text',
      description: 'Initial search value',
    },
    enableSpeciesSuggestion: {
      control: 'boolean',
      description: 'Enable species suggestion dropdown',
    },
    filter: {
      control: 'select',
      options: ['ORGANELLE', 'SPECIES', 'GENE'],
      description: 'Filter type for suggestions',
    },
    onSearch: {
      action: 'search submitted',
      description: 'Callback when search is submitted',
    },
  },
};

// Default SearchBar (without suggestions to avoid API issues)
export const Default = {
  args: {
    placeholder: 'Search...',
    name: 'search',
    enableSpeciesSuggestion: false,
    onSearch: (query) => console.log('Searching for:', query),
  },
};

// With Initial Value
export const WithInitialValue = {
  args: {
    placeholder: 'Search for species...',
    name: 'species-search',
    initialValue: 'Homo sapiens',
    onSearch: (query) => console.log('Searching for:', query),
  },
};

// Without Species Suggestions
export const WithoutSuggestions = {
  args: {
    placeholder: 'Simple search (no suggestions)',
    name: 'simple-search',
    enableSpeciesSuggestion: false,
    onSearch: (query) => console.log('Searching for:', query),
  },
};


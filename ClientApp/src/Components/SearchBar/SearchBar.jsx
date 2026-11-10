import { useState, useRef } from 'react';
import styles from './SearchBar.module.css';
import Input from '../Forms/Input/Input';
import Button from '../Button/Button';
import SpeciesSuggestions from '../SpeciesSuggestion/SpeciesSuggestion';
import Tooltip from '../Tooltip/Tooltip';



const SearchBar = ({ onSearch, placeholder, name, initialValue, enableSpeciesSuggestion = true, filter = 'ORGANELLE' }) => {
    
    const getIntialValue = () => initialValue ?? "";
    
    const [query, setQuery] = useState(getIntialValue());
    
    

    const suggestionsRef = useRef(null);

    const handleSearch = (e) => {
        console.log("query:", query);
        console.log("onSearch:", onSearch);
        e.preventDefault();
        if (!query.trim()) return;
        onSearch(query);
    };

    const handleFocus = () => {
        if (suggestionsRef.current) {
            suggestionsRef.current.showSuggestions();
        }
    };

    
    return (
        <form onSubmit={handleSearch} className={styles.searchForm}>

            <div className={styles.searchAlignment}>
                <div className={styles.searchInputWrapper}>

                    <Input
                        placeholder={placeholder}
                        name={name}
                        value={query}           // this must come from SearchBar state
                        onChange={(e) => setQuery(e.target.value)}
                        onFocus={handleFocus}
                        variant="primary"
                        autoComplete={"off"}
                        />

                    {enableSpeciesSuggestion && (<SpeciesSuggestions ref={suggestionsRef} query={query} setQuery={setQuery} filter={filter} />)}
                    
                </div>

                <Tooltip text={enableSpeciesSuggestion ? "Choose a suggested organism when available, or search the entered term." : "Search NCBI using the entered gene term."}>
                <Button
                    variant="primary"
                    color="blue"
                    label="Search"
                    type="submit"
                    />
                </Tooltip>
            </div>
        </form>
    );
};

export default SearchBar;


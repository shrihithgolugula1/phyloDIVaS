import Form from 'react-bootstrap/Form';
import styles from './Dropdown.module.css';

const Dropdown = ({
    name,
    placeholder,
    variant,
    options = [],
    value,
    onChange,
    required
}) => {
    return (
        <div>
            <Form.Select
                aria-label={placeholder}
                className={`${styles.dropdown} ${styles[variant]}`}
                name={name}
                value={value || ""} // controlled value
                onChange={onChange} // handle changes
                required={required}
            >
                <option value="" hidden>{placeholder}</option>
                {options.map((option, index) => (
                    <option key={index} value={option.value || option}>
                        {option.label || option}
                    </option>
                ))}
            </Form.Select>
        </div>
    );
};
export default Dropdown;
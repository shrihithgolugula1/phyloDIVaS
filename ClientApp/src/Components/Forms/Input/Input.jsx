import Form from 'react-bootstrap/Form';
import styles from './Input.module.css';

const Input = ({type="text", placeholder, name, variant, value, query, readOnly, disabled, onChange, autoComplete}) =>
{
   return (
       <div>
            <Form.Control
               type={type}
                placeholder={placeholder}
                name={name} 
                value={value || query} 
                onChange={onChange}
                readOnly={readOnly}
                disabled={disabled}
                autoComplete={autoComplete}
                className={`${styles.input} ${styles[variant]}`}/>
       </div>
   );
}

export default Input;

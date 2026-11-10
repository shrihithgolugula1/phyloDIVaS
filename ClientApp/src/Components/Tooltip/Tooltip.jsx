import styles from './Tooltip.module.css';

const Tooltip = ({ text, children, className = "" }) => {
    return (
        <div className={`${styles.tooltipWrapper} ${className}`}>
            {children}
            <span className={styles.tooltipText}>{text}</span>
        </div>
    );
};

export default Tooltip;

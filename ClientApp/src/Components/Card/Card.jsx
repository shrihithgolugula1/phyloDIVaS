import styles from './Card.module.css';

const Card = ({title, body, footer, icon}) => {
    return(
        <div className={styles.cardContainer}>
            {icon && <div className={styles.cardIcon}>{icon}</div>}
            
            {title && <h5 className={styles.cardTitle}>{title}</h5>}

            {body && <div className={styles.cardBody}>{body}</div>}

            {footer && <div className={styles.cardFooter}>{footer}</div>}
        </div>
    );
}

export default Card;
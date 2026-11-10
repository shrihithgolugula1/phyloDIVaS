import styles from './ComponentContainer.module.css';

const ComponentContainer = ({content}) => {
    return(
        // this can be used as a container for another component
        <div className={styles.container}>
            {content}
        </div>
    );
}

export default ComponentContainer;
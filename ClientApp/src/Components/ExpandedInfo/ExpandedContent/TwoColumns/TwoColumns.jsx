import styles from "./TwoColumns.module.css"


//takes two sections and displays them next to each other in two columns
const TwoColumns = ({sectionOne, sectionTwo}) => {
    return(
        <div className={styles.twoColumns}>
            <div className={styles.column}>
                {sectionOne}
            </div>
            <div className={styles.column}>
                {sectionTwo}
            </div>
        </div>
    );
};

export default TwoColumns;
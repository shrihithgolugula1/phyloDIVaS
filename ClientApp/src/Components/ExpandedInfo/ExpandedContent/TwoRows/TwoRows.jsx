import styles from "./TwoRows.module.css"


//takes two sections and displays one on top of the other. just the default for divs. meant to be used inside of TwoColumns section
const TwoRows = ({sectionOne, sectionTwo}) => {
    return(
        <div className={styles.twoRows}>
            <div className={styles.row}>
                {sectionOne}
            </div>
            <div className={styles.row}>
                {sectionTwo}
            </div>
        </div>
    );
};

export default TwoRows;
import styles from "./TextSection.module.css";


//a section with an optional title and a text block
const TextSection = ({title, text}) => {
    return(
        <div>
            {title && (
                <>
                    <h6 className={styles.header}>{title}</h6>
                </>
            )}

            <p>{text}</p>
        </div>
    );
};

export default TextSection;
import styles from "./RowSection.module.css";

const RowSection = ({title, data}) => {
  return (
    <div className={styles.rowsContainer}>
      {title && <h6 className={styles.title}>{title}</h6>}

      {data.map((item, index) => (
        <div key={index}>
          <div className={styles.sectionRow}>
            <p>{item.label}</p>
            <p>{item.value}</p>
          </div>
          
          {index < data.length - 1 ? (
            <hr className={styles.divider} />
          ) : null}
        </div>
      ))}
    </div>
  );
};

export default RowSection;
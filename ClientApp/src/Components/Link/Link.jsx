import { Link } from "react-router-dom";
import styles from "./Link.module.css";

const LinkTo = ({ label, linkTo, color }) => {
  const classNames = `${styles.button} ${styles[color]}`;

  return (
    <div>
      <Link to={linkTo} className={classNames}>
        {label}
      </Link>
    </div>
  );
};

export default LinkTo;

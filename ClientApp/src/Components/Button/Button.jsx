import { Link } from "react-router-dom";
import styles from "./Button.module.css";

const Button = ({
  label,
  linkTo,
  color = "blue",
  variant = "primary",
  type = "button",
  onClick,
  fullWidth = false,
}) => {
  const classNames = `
    ${styles.button}
    ${styles[variant]}
    ${styles[color]}
    ${fullWidth ? styles.fullWidth : ""}
  `;

  // If a linkTo is provided, render a Link
  if (linkTo) {
    return (
      <Link to={linkTo} className={classNames}>
        {label}
      </Link>
    );
  }

  // Otherwise, render a regular button (for forms or actions)
  return (
    <button type={type} onClick={onClick} className={classNames}>
      {label}
    </button>
  );
};

export default Button;
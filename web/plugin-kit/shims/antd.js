// Build-time shim: antd comes from the console instance (theme/context
// consistent, portals through the console's react-dom); plugins never
// bundle their own.
const F = globalThis.__CORDIS_CONSOLE;
if (!F) throw new Error("gocordis plugin: console facade missing — a bundled plugin must be loaded by a GoCordis console (webui/host), never standalone");
const A = F.antd;

export default A;
export const {
  Affix, Alert, AutoComplete, Avatar, Badge, Breadcrumb, Button, Calendar,
  Card, Carousel, Cascader, Checkbox, Col, Collapse, ConfigProvider,
  DatePicker, Descriptions, Divider, Drawer, Dropdown, Empty, Flex,
  FloatButton, Form, Image, Input, InputNumber, Layout, List, Menu, Modal,
  Pagination, Popconfirm, Popover, Progress, Radio, Rate, Result, Row,
  Segmented, Select, Skeleton, Slider, Space, Spin, Statistic, Steps,
  Switch, Table, Tabs, Tag, Timeline, Tooltip, Transfer, Tree, TreeSelect,
  Typography, Upload, theme,
} = A;
